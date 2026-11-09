using System;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Unattended start: when Documents\RevitClaude\autostart.json exists at Revit startup, the add-in
    /// opens the model it names (cloud or local, detached when asked) and starts the MCP server without
    /// any click. Used for workshops, CI smoke tests and remote sessions where nobody sits at the PC.
    /// The file is renamed to autostart.done.json afterwards, so the next normal launch is unaffected.
    ///
    /// {
    ///   "startServer": true,
    ///   "open": {
    ///     "cloud": { "region": "US", "projectGuid": "....", "modelGuid": "...." },   // or
    ///     "localPath": "C:\\models\\building.rvt",
    ///     "detach": "preserve" | "discard" | "none",
    ///     "worksets": "all" | "lastViewed" | "editable" | "none"
    ///   }
    /// }
    /// </summary>
    internal static class AutoStart
    {
        public static string ConfigPath => Path.Combine(WorkFolder(), "autostart.json");
        private static string LogPath => Path.Combine(WorkFolder(), "autostart.log");

        public static void Register(UIControlledApplication application)
        {
            if (!File.Exists(ConfigPath)) return;
            application.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;
            Log("autostart.json found; waiting for ApplicationInitialized.");
        }

        private static void OnApplicationInitialized(object sender, Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs e)
        {
            string done = Path.Combine(WorkFolder(), "autostart.done.json");
            try
            {
                var app = sender as Application;
                if (app == null) { Log("sender is not Application"); return; }
                var uiapp = new UIApplication(app);
                var cfg = JObject.Parse(File.ReadAllText(ConfigPath));

                var open = cfg["open"] as JObject;
                if (open != null)
                {
                    ModelPath modelPath = null;
                    var cloud = open["cloud"] as JObject;
                    string localPath = open.Value<string>("localPath");
                    if (cloud != null)
                    {
                        string region = cloud.Value<string>("region") ?? "US";
                        string regionId = region.Equals("EMEA", StringComparison.OrdinalIgnoreCase) ? ModelPathUtils.CloudRegionEMEA : ModelPathUtils.CloudRegionUS;
                        modelPath = ModelPathUtils.ConvertCloudGUIDsToCloudPath(regionId, Guid.Parse(cloud.Value<string>("projectGuid")), Guid.Parse(cloud.Value<string>("modelGuid")));
                    }
                    else if (!string.IsNullOrWhiteSpace(localPath))
                    {
                        modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(localPath);
                    }
                    if (modelPath != null)
                    {
                        var opts = new OpenOptions();
                        switch ((open.Value<string>("detach") ?? "preserve").ToLowerInvariant())
                        {
                            case "discard": opts.DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets; break;
                            case "none": opts.DetachFromCentralOption = DetachFromCentralOption.DoNotDetach; break;
                            default: opts.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets; break;
                        }
                        var wsOpt = WorksetConfigurationOption.OpenAllWorksets;
                        switch ((open.Value<string>("worksets") ?? "all").ToLowerInvariant())
                        {
                            case "lastviewed": wsOpt = WorksetConfigurationOption.OpenLastViewed; break;
                            case "editable": wsOpt = WorksetConfigurationOption.OpenAllWorksets; break;
                            case "none": wsOpt = WorksetConfigurationOption.CloseAllWorksets; break;
                        }
                        opts.SetOpenWorksetsConfiguration(new WorksetConfiguration(wsOpt));
                        Log("Opening " + ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath) + " detach=" + opts.DetachFromCentralOption);
                        var uidoc = uiapp.OpenAndActivateDocument(modelPath, opts, false);
                        Log("Opened: " + uidoc.Document.Title + " (workshared=" + uidoc.Document.IsWorkshared + ")");
                    }
                }

                if (cfg.Value<bool?>("startServer") ?? true)
                {
                    var service = SocketService.Instance;
                    if (!service.IsRunning)
                    {
                        service.Initialize(uiapp);
                        service.Start();
                    }
                    Log("MCP server running: " + service.IsRunning + " on port " + service.Port);
                }
            }
            catch (Exception ex)
            {
                Log("FAILED: " + ex);
            }
            finally
            {
                try { if (File.Exists(done)) File.Delete(done); File.Move(ConfigPath, done); } catch (Exception ex) { Log("rename failed: " + ex.Message); }
            }
        }

        private static string WorkFolder()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RevitClaude");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Log(string text)
        {
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text + Environment.NewLine); } catch { }
        }
    }
}
