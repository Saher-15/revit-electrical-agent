using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using revit_mcp_plugin.Core;

namespace revit_mcp_plugin.Chat
{
    /// <summary>
    /// Registers the "Claude" dockable pane and keeps the pieces it needs from Revit:
    /// an ExternalEvent that starts the MCP socket server from inside the pane.
    /// </summary>
    public static class ChatPane
    {
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("6F1C2B9E-3D4A-4C2B-9B7E-2A1D5C8E7F10"));

        private static ChatPaneView _view;
        private static ExternalEvent _startServerEvent;
        private static StartServerHandler _startServerHandler;

        public static ChatPaneView View => _view;

        /// <summary>Call from OnStartup.</summary>
        public static void Register(UIControlledApplication application)
        {
            _view = new ChatPaneView();
            application.RegisterDockablePane(PaneId, "Claude", new Provider(_view));
            application.Idling += OnFirstIdle;
        }

        public static void Shutdown()
        {
            try { _view?.Shutdown(); } catch { }
        }

        // ExternalEvent.Create needs a valid API context; the first Idling call gives one.
        private static void OnFirstIdle(object sender, IdlingEventArgs e)
        {
            if (sender is UIApplication uiApp)
            {
                uiApp.Idling -= OnFirstIdle;
                try
                {
                    _startServerHandler = new StartServerHandler();
                    _startServerEvent = ExternalEvent.Create(_startServerHandler);
                }
                catch { }
            }
        }

        /// <summary>Asks Revit to start the MCP socket server (port 8080). Calls back on Revit's UI thread.</summary>
        public static bool RequestStartServer(Action<bool> done)
        {
            if (SocketService.Instance.IsRunning) { done?.Invoke(true); return true; }
            if (_startServerEvent == null) return false;
            _startServerHandler.Done = done;
            var r = _startServerEvent.Raise();
            return r == ExternalEventRequest.Accepted || r == ExternalEventRequest.Pending;
        }

        /// <summary>Starts the server directly (from a command, already in API context).</summary>
        public static bool StartServer(UIApplication uiApp)
        {
            var service = SocketService.Instance;
            if (service.IsRunning) return true;
            service.Initialize(uiApp);
            service.Start();
            return service.IsRunning;
        }

        private class StartServerHandler : IExternalEventHandler
        {
            public Action<bool> Done;
            public void Execute(UIApplication app)
            {
                bool ok;
                try { ok = StartServer(app); } catch { ok = false; }
                var d = Done; Done = null;
                d?.Invoke(ok);
            }
            public string GetName() => "Claude chat: start MCP server";
        }

        private class Provider : IDockablePaneProvider
        {
            private readonly ChatPaneView _view;
            public Provider(ChatPaneView view) { _view = view; }

            public void SetupDockablePane(DockablePaneProviderData data)
            {
                data.FrameworkElement = _view;
                data.InitialState = new DockablePaneState
                {
                    DockPosition = DockPosition.Tabbed,
                    TabBehind = DockablePanes.BuiltInDockablePanes.PropertiesPalette,
                };
                data.VisibleByDefault = false;
            }
        }
    }

    /// <summary>Ribbon button "Claude Chat": starts the MCP server and shows the chat pane.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShowChatPane : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!ChatPane.StartServer(commandData.Application))
                    TaskDialog.Show("Claude", "The Revit MCP server could not be started on port 8080. Is another Revit session already using it?\nThe chat opens anyway, but Claude cannot reach the model.");
                OpenClaude.EnsureClaudeMd(ChatSettings.Load().ResolvedWorkingFolder);
                var pane = commandData.Application.GetDockablePane(ChatPane.PaneId);
                pane.Show();
                ChatPane.View?.FocusInput();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
