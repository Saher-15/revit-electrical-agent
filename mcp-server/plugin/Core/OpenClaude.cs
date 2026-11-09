using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Ribbon button "Open Claude": starts the MCP server if needed, then opens a PowerShell
    /// window running Claude Code in a dedicated working folder (Documents\RevitClaude).
    /// The folder gets a CLAUDE.md (electrical-only rules) the first time, copied from the add-in folder.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class OpenClaude : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var service = SocketService.Instance;
                bool startedNow = false;
                if (!service.IsRunning)
                {
                    service.Initialize(commandData.Application);
                    service.Start();
                    startedNow = service.IsRunning;
                    if (!startedNow)
                    {
                        TaskDialog.Show("Revit MCP", "The MCP server could not be started on port 8080. Is another Revit session already using it?");
                        return Result.Failed;
                    }
                }

                string workDir = GetWorkingFolder();
                EnsureClaudeMd(workDir);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoExit -ExecutionPolicy Bypass -Command \"" +
                                "$Host.UI.RawUI.WindowTitle = 'Claude - Revit MCP'; " +
                                "Set-Location -LiteralPath '" + workDir.Replace("'", "''") + "'; " +
                                "if (Get-Command claude -ErrorAction SilentlyContinue) { claude } " +
                                "else { Write-Host 'Claude Code (claude) was not found in PATH. Install it with: npm install -g @anthropic-ai/claude-code' -ForegroundColor Red }\"",
                    WorkingDirectory = workDir,
                    UseShellExecute = true,
                };
                Process.Start(psi);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Revit MCP", "Could not open Claude:\n" + ex.Message);
                return Result.Failed;
            }
        }

        internal static string GetWorkingFolder()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string dir = Path.Combine(docs, "RevitClaude");
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static void EnsureClaudeMd(string workDir)
        {
            try
            {
                string target = Path.Combine(workDir, "CLAUDE.md");
                if (File.Exists(target)) return;
                string addinDir = Path.GetDirectoryName(typeof(OpenClaude).Assembly.Location) ?? "";
                string source = Path.Combine(addinDir, "CLAUDE.md");
                if (File.Exists(source)) File.Copy(source, target);
            }
            catch { /* optional convenience only */ }
        }
    }
}
