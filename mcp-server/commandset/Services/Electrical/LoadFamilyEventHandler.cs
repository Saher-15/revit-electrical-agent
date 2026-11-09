using System.IO;
using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Electrical
{
    /// <summary>Loads .rfa families from a path or by searching the Revit libraries.</summary>
    public class LoadFamilyEventHandler : ElectricalHandlerBase<LoadFamilyInfo, LoadFamilyResult>
    {
        public override string GetName() => "load_family";

        protected override AIResult<LoadFamilyResult> Run()
        {
            var info = Input ?? new LoadFamilyInfo();
            var result = new LoadFamilyResult();

            var files = new List<string>();
            if (!string.IsNullOrWhiteSpace(info.FilePath))
            {
                if (!File.Exists(info.FilePath)) return Fail($"File not found: {info.FilePath}");
                files.Add(info.FilePath);
            }
            else if (!string.IsNullOrWhiteSpace(info.NameFilter))
            {
                files = Search(info.NameFilter, Math.Max(1, info.MaxResults));
                if (files.Count == 0) return Fail($"No .rfa file named like '{info.NameFilter}' in the Revit libraries.");
                if (files.Count > 1 && !info.LoadAllMatches)
                {
                    result.Candidates = files;
                    return Ok(result, $"{files.Count} files match. Call again with filePath for one of them, or loadAllMatches=true.");
                }
            }
            else
            {
                return Fail("Give filePath or nameFilter.");
            }

            using (var t = new Transaction(Doc, "MCP: load family"))
            {
                t.Start();
                ElectricalUtils.MakeSilent(t);
                foreach (var file in files)
                {
                    try
                    {
                        if (Doc.LoadFamily(file, new OverwriteLoadOptions(), out Family family) && family != null)
                        {
                            result.LoadedFamilies.Add(family.Name);
                            foreach (ElementId sid in family.GetFamilySymbolIds())
                            {
                                if (Doc.GetElement(sid) is FamilySymbol fs)
                                    result.LoadedTypes.Add(ElectricalUtils.ToTypeInfo(fs));
                            }
                        }
                        else
                        {
                            // Already loaded with identical content: report existing types.
                            string name = Path.GetFileNameWithoutExtension(file);
                            var existing = new FilteredElementCollector(Doc).OfClass(typeof(Family)).Cast<Family>()
                                .FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                result.LoadedFamilies.Add(existing.Name);
                                foreach (ElementId sid in existing.GetFamilySymbolIds())
                                    if (Doc.GetElement(sid) is FamilySymbol fs) result.LoadedTypes.Add(ElectricalUtils.ToTypeInfo(fs));
                                Warnings.Add($"'{name}' was already loaded.");
                            }
                            else
                            {
                                Warnings.Add($"Revit did not load '{file}'.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Warnings.Add($"Failed to load '{file}': {ex.Message}");
                    }
                }
                t.Commit();
            }

            if (result.LoadedFamilies.Count == 0) return Fail("No family was loaded.");
            return Ok(result, $"Loaded {result.LoadedFamilies.Count} family(ies) with {result.LoadedTypes.Count} type(s).");
        }

        private List<string> Search(string filter, int max)
        {
            var roots = new List<string>();
            try
            {
                foreach (var kv in UiApp.Application.GetLibraryPaths())
                    if (Directory.Exists(kv.Value)) roots.Add(kv.Value);
            }
            catch { }
            string version = UiApp.Application.VersionNumber;
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string def = Path.Combine(programData, "Autodesk", $"RVT {version}", "Libraries");
            if (Directory.Exists(def)) roots.Add(def);

            var found = new List<string>();
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(root, "*.rfa", SearchOption.AllDirectories))
                    {
                        if (Path.GetFileName(f).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found.Add(f);
                            if (found.Count >= max) return found;
                        }
                    }
                }
                catch { /* unreadable folder */ }
            }
            return found;
        }

        private class OverwriteLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = false;
                return true;
            }
        }
    }
}
