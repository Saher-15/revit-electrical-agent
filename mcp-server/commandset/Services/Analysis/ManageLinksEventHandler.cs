using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Analysis;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Analysis
{
    /// <summary>
    /// Lists the Revit links of the open model and reloads the ones that are not loaded. A model opened detached
    /// and saved locally usually loses its cloud links ("Not Shared"), and without the architect link there are
    /// no rooms, furniture or walls for the design engine. Reload/LoadFrom run outside a transaction, as the API requires.
    /// </summary>
    public class ManageLinksEventHandler : ElectricalHandlerBase<ManageLinksRequest, ManageLinksResult>
    {
        public override string GetName() => "manage_links";

        protected override AIResult<ManageLinksResult> Run()
        {
            var req = Input ?? new ManageLinksRequest();
            bool reload = string.Equals(req.Action, "reload", StringComparison.OrdinalIgnoreCase);
            var result = new ManageLinksResult();

            var instanceCounts = new FilteredElementCollector(Doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                .GroupBy(i => i.GetTypeId().GetValue()).ToDictionary(g => g.Key, g => g.Count());

            foreach (var type in new FilteredElementCollector(Doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
            {
                var info = Describe(type);
                instanceCounts.TryGetValue(info.TypeId, out int n);
                info.Instances = n;
                bool wanted = req.Names == null || req.Names.Count == 0 ||
                              req.Names.Any(k => !string.IsNullOrWhiteSpace(k) && info.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                if (reload && wanted && (!req.OnlyUnloaded || !info.Loaded))
                    ReloadOne(type, info, req.LocalFolder);
                result.Links.Add(info);
            }

            result.Loaded = result.Links.Count(l => l.Loaded);
            result.NotLoaded = result.Links.Count - result.Loaded;
            result.Reloaded = result.Links.Count(l => l.Action == "reloaded" || l.Action == "loadedFromLocal");
            result.Failed = result.Links.Count(l => l.Action == "failed");
            string msg = $"{result.Links.Count} Revit links: {result.Loaded} loaded, {result.NotLoaded} not loaded" +
                         (reload ? $"; reloaded {result.Reloaded}, failed {result.Failed}" : "") + ".";
            return Ok(result, msg);
        }

        private LinkInfo Describe(RevitLinkType type)
        {
            var info = new LinkInfo { TypeId = type.Id.GetValue(), Name = type.Name, PathType = "unknown" };
            try
            {
                var status = type.GetLinkedFileStatus();
                info.Status = status.ToString();
                info.Loaded = status == LinkedFileStatus.Loaded;
            }
            catch (Exception ex) { info.Status = "?"; info.Error = ex.Message; }
            try
            {
                if (type.IsExternalFileReference())
                {
                    var efr = type.GetExternalFileReference();
                    var mp = efr.GetAbsolutePath();
                    info.Path = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                    info.PathType = mp.CloudPath ? "cloud" : mp.ServerPath ? "server" : "file";
                }
                else
                {
                    var refs = type.GetExternalResourceReferences();
                    if (refs != null && refs.Count > 0)
                    {
                        var r = refs.Values.First();
                        info.Path = r.InSessionPath;
                        info.PathType = "cloud";
                    }
                }
            }
            catch (Exception ex) { if (info.Error == null) info.Error = ex.Message; }
            return info;
        }

        private void ReloadOne(RevitLinkType type, LinkInfo info, string localFolder)
        {
            try
            {
                string local = FindLocalFile(type.Name, localFolder);
                LinkLoadResult res;
                if (local != null)
                {
                    res = type.LoadFrom(ModelPathUtils.ConvertUserVisiblePathToModelPath(local), new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
                    info.Action = res.LoadResult == LinkLoadResultType.LinkLoaded ? "loadedFromLocal" : "failed";
                    info.Path = local; info.PathType = "file";
                }
                else
                {
                    res = type.Reload();
                    info.Action = res.LoadResult == LinkLoadResultType.LinkLoaded ? "reloaded" : "failed";
                }
                if (info.Action == "failed") info.Error = res.LoadResult.ToString();
                var status = type.GetLinkedFileStatus();
                info.Status = status.ToString();
                info.Loaded = status == LinkedFileStatus.Loaded;
            }
            catch (Exception ex)
            {
                info.Action = "failed";
                info.Error = ex.Message;
                Warnings.Add($"Link '{type.Name}': {ex.Message}");
            }
        }

        /// <summary>Finds &lt;localFolder&gt;\&lt;link name&gt;(.rvt) if it exists; the link type name is the file name.</summary>
        private static string FindLocalFile(string linkName, string localFolder)
        {
            if (string.IsNullOrWhiteSpace(localFolder) || !Directory.Exists(localFolder)) return null;
            string name = linkName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) ? linkName : linkName + ".rvt";
            string candidate = Path.Combine(localFolder, name);
            if (File.Exists(candidate)) return candidate;
            return Directory.EnumerateFiles(localFolder, "*.rvt", SearchOption.AllDirectories)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
