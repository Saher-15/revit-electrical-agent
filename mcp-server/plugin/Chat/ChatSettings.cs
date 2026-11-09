using System;
using System.IO;
using Newtonsoft.Json;

namespace revit_mcp_plugin.Chat
{
    /// <summary>
    /// Settings of the Claude chat pane, stored in Documents\RevitClaude\chat-settings.json.
    /// The file is created with defaults on first use and can be edited by hand.
    /// </summary>
    public class ChatSettings
    {
        /// <summary>Full path to claude.exe / claude.cmd. Empty = find it automatically.</summary>
        public string ClaudePath { get; set; } = "";

        /// <summary>Model alias passed to --model (opus, sonnet, haiku). Empty = Claude Code default.</summary>
        public string Model { get; set; } = "";

        /// <summary>Tools Claude may use without asking. Everything else is refused in the chat pane.</summary>
        public string AllowedTools { get; set; } = "mcp__mcp-server-for-revit,Read,Glob,Grep,TodoWrite";

        /// <summary>Tools that are always blocked in the chat pane.</summary>
        public string DisallowedTools { get; set; } = "Bash,Write,Edit,MultiEdit,NotebookEdit,WebFetch,WebSearch";

        /// <summary>Folder Claude runs in (holds CLAUDE.md). Empty = Documents\RevitClaude.</summary>
        public string WorkingFolder { get; set; } = "";

        [JsonIgnore]
        public string ResolvedWorkingFolder
        {
            get
            {
                string dir = string.IsNullOrWhiteSpace(WorkingFolder) ? DefaultFolder : WorkingFolder;
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RevitClaude");

        private static string FilePath => Path.Combine(DefaultFolder, "chat-settings.json");

        public static ChatSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonConvert.DeserializeObject<ChatSettings>(File.ReadAllText(FilePath));
                    if (s != null) return s;
                }
            }
            catch { /* fall back to defaults */ }
            var settings = new ChatSettings();
            settings.Save();
            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DefaultFolder);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch { /* settings are a convenience */ }
        }
    }
}
