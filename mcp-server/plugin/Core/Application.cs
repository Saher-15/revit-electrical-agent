using System;
using Autodesk.Revit.UI;
using System.Reflection;
using System.Windows.Media.Imaging;



namespace revit_mcp_plugin.Core
{
    public class Application : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            RibbonPanel mcpPanel = application.CreateRibbonPanel("Revit MCP Plugin");

            PushButtonData pushButtonData = new PushButtonData("ID_EXCMD_TOGGLE_REVIT_MCP", "Revit MCP\r\n Switch",
                Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.MCPServiceConnection");
            pushButtonData.ToolTip = "Open / Close mcp server";
            pushButtonData.Image = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/icon-16.png", UriKind.RelativeOrAbsolute));
            pushButtonData.LargeImage = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/icon-32.png", UriKind.RelativeOrAbsolute));
            mcpPanel.AddItem(pushButtonData);

            PushButtonData mcp_settings_pushButtonData = new PushButtonData("ID_EXCMD_MCP_SETTINGS", "Settings",
                Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.Settings");
            mcp_settings_pushButtonData.ToolTip = "MCP Settings";
            mcp_settings_pushButtonData.Image = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/settings-16.png", UriKind.RelativeOrAbsolute));
            mcp_settings_pushButtonData.LargeImage = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/settings-32.png", UriKind.RelativeOrAbsolute));
            mcpPanel.AddItem(mcp_settings_pushButtonData);

            PushButtonData openClaude = new PushButtonData("ID_EXCMD_OPEN_CLAUDE", "Open\r\nClaude",
                Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.OpenClaude");
            openClaude.ToolTip = "Start the MCP server (if needed) and open Claude Code in a PowerShell window";
            openClaude.LongDescription = "Opens Claude Code in Documents\\RevitClaude, connected to this Revit session. Electrical-only rules are loaded from CLAUDE.md in that folder.";
            openClaude.Image = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/claude-16.png", UriKind.RelativeOrAbsolute));
            openClaude.LargeImage = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/claude-32.png", UriKind.RelativeOrAbsolute));
            mcpPanel.AddItem(openClaude);

            // Claude chat inside Revit (dockable pane).
            PushButtonData chatButton = new PushButtonData("ID_EXCMD_CLAUDE_CHAT", "Claude\r\nChat",
                Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Chat.ShowChatPane");
            chatButton.ToolTip = "Open the Claude chat pane inside Revit";
            chatButton.LongDescription = "Starts the MCP server (if needed) and opens a chat pane connected to this Revit session. Uses Claude Code on this PC (your claude.ai sign-in), working in Documents\\RevitClaude.";
            chatButton.Image = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/claude-16.png", UriKind.RelativeOrAbsolute));
            chatButton.LargeImage = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/claude-32.png", UriKind.RelativeOrAbsolute));
            mcpPanel.AddItem(chatButton);

            try { revit_mcp_plugin.Chat.ChatPane.Register(application); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Claude chat pane: " + ex); }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            revit_mcp_plugin.Chat.ChatPane.Shutdown();
            try
            {
                if (SocketService.Instance.IsRunning)
                {
                    SocketService.Instance.Stop();
                }
            }
            catch { }

            return Result.Succeeded;
        }
    }
}
