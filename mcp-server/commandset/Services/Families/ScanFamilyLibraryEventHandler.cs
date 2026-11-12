using System.IO;
using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Services.Electrical;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Opens every .rfa under a folder (read-only, closed without saving) and reports category, placement type
    /// (WorkPlaneBased / OneLevelBasedHosted...) and connector count. Nothing is loaded into the project.
    /// </summary>
    public class ScanFamilyLibraryEventHandler : ElectricalHandlerBase<ScanFamilyLibraryRequest, ScanFamilyLibraryResult>
    {
        public override string GetName() => "scan_family_library";

        protected override AIResult<ScanFamilyLibraryResult> Run()
        {
            var req = Input ?? new ScanFamilyLibraryRequest();
            if (string.IsNullOrWhiteSpace(req.Folder) || !Directory.Exists(req.Folder)) return Fail($"Folder not found: {req.Folder}");
            var result = new ScanFamilyLibraryResult { Folder = req.Folder };
            var files = Directory.EnumerateFiles(req.Folder, "*.rfa", req.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => req.IncludeBackups || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"\.\d{4}\.rfa$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .OrderBy(f => f).ToList();
            result.Total = files.Count;
            // The MCP client gives up after 2 min, so the scan is paged: skip/maxFiles, nextSkip says where to continue.
            files = files.Skip(req.Skip).Take(req.MaxFiles).ToList();
            if (req.Skip + files.Count < result.Total) result.NextSkip = req.Skip + files.Count;
            var app = UiApp.Application;
            foreach (var file in files)
            {
                var row = new ScannedFamily { File = file.Substring(req.Folder.Length).TrimStart(Path.DirectorySeparatorChar, '/') };
                Document fam = null;
                try
                {
                    fam = app.OpenDocumentFile(file);
                    if (fam == null || !fam.IsFamilyDocument) { row.Error = "not a family document"; }
                    else
                    {
                        row.Category = fam.OwnerFamily?.FamilyCategory?.Name;
                        row.Placement = fam.OwnerFamily?.FamilyPlacementType.ToString();
                        row.Connectors = new FilteredElementCollector(fam).OfClass(typeof(ConnectorElement)).GetElementCount();
                        row.Types = fam.FamilyManager.Types.Size;
                    }
                }
                catch (Exception ex) { row.Error = ex.Message; }
                finally { try { fam?.Close(false); } catch { } }
                result.Families.Add(row);
            }
            result.Scanned = result.Families.Count;
            result.ByPlacement = result.Families.GroupBy(r => r.Placement ?? "error").ToDictionary(g => g.Key, g => g.Count());
            return Ok(result, $"{result.Scanned} of {result.Total} families scanned" + string.Join("", result.ByPlacement.Select(kv => $", {kv.Key}: {kv.Value}")));
        }
    }
}
