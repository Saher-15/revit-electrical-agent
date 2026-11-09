using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.Chat
{
    /// <summary>
    /// Drives the local Claude Code CLI in headless mode:
    ///   claude -p --input-format stream-json --output-format stream-json --verbose --include-partial-messages
    /// One long-running process holds the conversation (and keeps the Revit MCP server warm).
    /// Uses the claude.ai login of Claude Code on this PC, so no API key is needed.
    /// All events are raised on a background thread.
    /// </summary>
    public sealed class ClaudeSession : IDisposable
    {
        public const string RevitServerPrefix = "mcp__mcp-server-for-revit__";

        private readonly object _lock = new object();
        private Process _process;
        private StreamWriter _stdin;
        private readonly StringBuilder _stderr = new StringBuilder();
        private bool _streamedTextInMessage;
        private bool _stopRequested;

        public ChatSettings Settings { get; }
        public string SessionId { get; private set; }
        public bool IsBusy { get; private set; }
        public bool IsRunning { get { lock (_lock) return _process != null && !_process.HasExited; } }

        /// <summary>Streaming piece of assistant text.</summary>
        public event Action<string> TextDelta;
        /// <summary>A whole assistant text block (only when it was not streamed).</summary>
        public event Action<string> AssistantText;
        /// <summary>tool use id, display name, input JSON.</summary>
        public event Action<string, string, string> ToolStarted;
        /// <summary>tool use id, is error, result text.</summary>
        public event Action<string, bool, string> ToolFinished;
        /// <summary>is error, duration ms, result text.</summary>
        public event Action<bool, long, string> TurnCompleted;
        /// <summary>Model name and MCP server statuses (name -> status) when Claude starts.</summary>
        public event Action<string, IDictionary<string, string>> Initialized;
        public event Action<string> Error;
        public event Action<bool> BusyChanged;

        public ClaudeSession(ChatSettings settings)
        {
            Settings = settings;
        }

        /// <summary>Sends one user message. Starts (or resumes) the Claude process when needed.</summary>
        public void Send(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_lock)
            {
                if (_process == null || _process.HasExited) StartProcess();
                var msg = new JObject
                {
                    ["type"] = "user",
                    ["message"] = new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } }
                    }
                };
                _stdin.Write(msg.ToString(Formatting.None) + "\n");
                _stdin.Flush();
            }
            SetBusy(true);
        }

        /// <summary>Stops the current answer. The conversation is kept and resumed with the next message.</summary>
        public void Stop()
        {
            _stopRequested = true;
            KillProcess();
            SetBusy(false);
        }

        /// <summary>Starts a brand-new conversation.</summary>
        public void Reset()
        {
            _stopRequested = true;
            KillProcess();
            SessionId = null;
            SetBusy(false);
        }

        public void Dispose() => KillProcess();

        private void SetBusy(bool busy)
        {
            if (IsBusy == busy) return;
            IsBusy = busy;
            BusyChanged?.Invoke(busy);
        }

        // ----------------------------------------------------------------- process

        private void StartProcess()
        {
            string claude = FindClaude(Settings.ClaudePath);
            if (claude == null)
                throw new InvalidOperationException(
                    "Claude Code (claude) was not found. Install it (npm install -g @anthropic-ai/claude-code), " +
                    "sign in once by running 'claude' in PowerShell, or set ClaudePath in Documents\\RevitClaude\\chat-settings.json.");

            var args = new List<string>
            {
                "-p", "--input-format", "stream-json", "--output-format", "stream-json",
                "--verbose", "--include-partial-messages"
            };
            if (!string.IsNullOrWhiteSpace(Settings.Model)) { args.Add("--model"); args.Add(Safe(Settings.Model)); }
            if (!string.IsNullOrWhiteSpace(SessionId)) { args.Add("--resume"); args.Add(Safe(SessionId)); }
            if (!string.IsNullOrWhiteSpace(Settings.DisallowedTools)) { args.Add("--disallowedTools"); args.Add(Safe(Settings.DisallowedTools)); }
            // Variadic option last.
            if (!string.IsNullOrWhiteSpace(Settings.AllowedTools)) { args.Add("--allowedTools"); args.Add(Safe(Settings.AllowedTools)); }
            string argLine = string.Join(" ", args);

            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                StandardInputEncoding = new UTF8Encoding(false),
                WorkingDirectory = Settings.ResolvedWorkingFolder,
            };
            if (claude.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || claude.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                psi.Arguments = "/d /s /c \"\"" + claude + "\" " + argLine + "\"";
            }
            else
            {
                psi.FileName = claude;
                psi.Arguments = argLine;
            }
            psi.Environment["CLAUDE_CODE_ENTRYPOINT"] = "revit-chat-pane";

            _stderr.Clear();
            _streamedTextInMessage = false;
            _stopRequested = false;
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.Start();
            _process = p;
            _stdin = p.StandardInput;
            _stdin.AutoFlush = false;

            new Thread(() => ReadStdout(p)) { IsBackground = true, Name = "claude-stdout" }.Start();
            new Thread(() => ReadStderr(p)) { IsBackground = true, Name = "claude-stderr" }.Start();
        }

        private static string Safe(string value)
        {
            // Values are passed unquoted (they also travel through cmd.exe), so allow only plain characters.
            var sb = new StringBuilder();
            foreach (char c in value)
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ',' || c == '.' || c == '*' || c == '[' || c == ']' || c == ':')
                    sb.Append(c);
            return sb.ToString();
        }

        private void KillProcess()
        {
            Process p;
            lock (_lock)
            {
                p = _process;
                _process = null;
                _stdin = null;
            }
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(true); } catch { }
            try { p.Dispose(); } catch { }
        }

        private void ReadStderr(Process p)
        {
            try
            {
                string line;
                while ((line = p.StandardError.ReadLine()) != null)
                    lock (_stderr) { if (_stderr.Length < 8000) _stderr.AppendLine(line); }
            }
            catch { }
        }

        private void ReadStdout(Process p)
        {
            try
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] != '{') continue;
                    JObject evt;
                    try { evt = JObject.Parse(line); } catch { continue; }
                    try { Handle(evt); } catch (Exception ex) { Error?.Invoke("Chat pane error: " + ex.Message); }
                }
            }
            catch { }

            // Process ended.
            bool current;
            lock (_lock) current = ReferenceEquals(_process, p);
            if (current && !_stopRequested)
            {
                try { p.WaitForExit(2000); } catch { }
                string err;
                lock (_stderr) err = _stderr.ToString().Trim();
                int code = -1;
                try { code = p.ExitCode; } catch { }
                if (IsBusy || code != 0)
                    Error?.Invoke("Claude stopped unexpectedly" + (code != -1 ? " (exit code " + code + ")" : "") +
                                  (err.Length > 0 ? ":\n" + err : ". Check that 'claude' runs in PowerShell and that you are signed in."));
                lock (_lock) { if (ReferenceEquals(_process, p)) { _process = null; _stdin = null; } }
                SetBusy(false);
            }
        }

        // ----------------------------------------------------------------- events

        private void Handle(JObject evt)
        {
            string type = (string)evt["type"];
            switch (type)
            {
                case "system":
                    if ((string)evt["subtype"] == "init")
                    {
                        SessionId = (string)evt["session_id"] ?? SessionId;
                        var servers = new Dictionary<string, string>();
                        if (evt["mcp_servers"] is JArray arr)
                            foreach (var s in arr)
                                servers[(string)s["name"] ?? "?"] = (string)s["status"] ?? "?";
                        Initialized?.Invoke((string)evt["model"] ?? "", servers);
                    }
                    break;

                case "stream_event":
                    var se = evt["event"] as JObject;
                    string seType = (string)se?["type"];
                    if (seType == "message_start") _streamedTextInMessage = false;
                    else if (seType == "content_block_delta" && (string)se["delta"]?["type"] == "text_delta")
                    {
                        _streamedTextInMessage = true;
                        TextDelta?.Invoke((string)se["delta"]["text"] ?? "");
                    }
                    break;

                case "assistant":
                    if (evt["message"]?["content"] is JArray blocks)
                        foreach (var b in blocks)
                        {
                            string bt = (string)b["type"];
                            if (bt == "text" && !_streamedTextInMessage)
                                AssistantText?.Invoke((string)b["text"] ?? "");
                            else if (bt == "tool_use")
                                ToolStarted?.Invoke((string)b["id"], DisplayToolName((string)b["name"]),
                                    b["input"]?.ToString(Formatting.Indented) ?? "");
                        }
                    break;

                case "user":
                    if (evt["message"]?["content"] is JArray ublocks)
                        foreach (var b in ublocks)
                            if ((string)b["type"] == "tool_result")
                                ToolFinished?.Invoke((string)b["tool_use_id"], (bool?)b["is_error"] ?? false, ToolResultText(b["content"]));
                    break;

                case "result":
                    SessionId = (string)evt["session_id"] ?? SessionId;
                    bool isError = (bool?)evt["is_error"] ?? ((string)evt["subtype"] != "success");
                    long ms = (long?)evt["duration_ms"] ?? 0;
                    string resultText = (string)evt["result"] ?? (string)evt["subtype"] ?? "";
                    SetBusy(false);
                    TurnCompleted?.Invoke(isError, ms, resultText);
                    break;
            }
        }

        public static string DisplayToolName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "tool";
            if (name.StartsWith(RevitServerPrefix)) return name.Substring(RevitServerPrefix.Length);
            if (name.StartsWith("mcp__"))
            {
                int i = name.IndexOf("__", 5, StringComparison.Ordinal);
                return i > 0 ? name.Substring(i + 2) : name;
            }
            return name;
        }

        private static string ToolResultText(JToken content)
        {
            if (content == null) return "";
            if (content.Type == JTokenType.String) return (string)content;
            if (content is JArray arr)
                return string.Join("\n", arr.Where(x => (string)x["type"] == "text").Select(x => (string)x["text"]));
            return content.ToString();
        }

        // ----------------------------------------------------------------- locate claude

        public static string FindClaude(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

            var candidates = new List<string>();
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(Path.PathSeparator).Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                candidates.Add(Path.Combine(dir.Trim('"'), "claude.exe"));
                candidates.Add(Path.Combine(dir.Trim('"'), "claude.cmd"));
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            candidates.Add(Path.Combine(home, ".local", "bin", "claude.exe"));
            candidates.Add(Path.Combine(appData, "npm", "claude.cmd"));
            candidates.Add(Path.Combine(home, ".claude", "local", "claude.cmd"));

            foreach (string c in candidates)
            {
                try { if (File.Exists(c)) return c; } catch { }
            }
            return null;
        }
    }
}
