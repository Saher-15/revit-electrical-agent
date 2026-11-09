using System.IO;
using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Builds a new family from a template: solids, a 2D plan symbol, parameters, an electrical connector and types.
    /// Saves it as .rfa (Documents\RevitClaude\families by default) and loads it into the project.
    /// </summary>
    public class CreateFamilyEventHandler : ElectricalHandlerBase<CreateFamilyRequest, FamilyResult>
    {
        public override string GetName() => "create_family";

        protected override AIResult<FamilyResult> Run()
        {
            var req = Input ?? new CreateFamilyRequest();
            if (string.IsNullOrWhiteSpace(req.Name)) return Fail("name is required.");
            var result = new FamilyResult { FamilyName = DocumentationUtils.SafeFileName(req.Name) };

            string folder = string.IsNullOrWhiteSpace(req.SaveFolder) ? DocumentationUtils.DefaultFolder("families") : req.SaveFolder;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, result.FamilyName + ".rfa");
            if (File.Exists(path) && !req.Overwrite) return Fail($"{path} exists. Pass overwrite=true or another name.");
            result.FilePath = path;

            var app = UiApp.Application;
            string template = FamilyBuildUtils.FindTemplate(app, req.Template, out var candidates);
            if (template == null)
            {
                result.TemplateCandidates = candidates;
                return Fail($"No family template matches '{req.Template}'. Available: {string.Join(", ", candidates)}");
            }
            result.Template = Path.GetFileName(template);
            if (candidates.Count > 1) result.TemplateCandidates = candidates;

            Document famDoc = app.NewFamilyDocument(template);
            if (famDoc == null) return Fail($"Revit could not open template {template}.");
            try
            {
                var mgr = famDoc.FamilyManager;
                var t = FamilyBuildUtils.StartSilent(famDoc, "MCP: build family", out var pre);

                if (!string.IsNullOrWhiteSpace(req.Category))
                {
                    if (ElectricalUtils.TryParseCategory(req.Category, out var bic) && Category.GetCategory(famDoc, bic) is Category cat)
                    {
                        try { famDoc.OwnerFamily.FamilyCategory = cat; } catch (Exception ex) { Warnings.Add($"Category not changed: {ex.Message}"); }
                    }
                    else Warnings.Add($"Unknown category '{req.Category}'.");
                }
                FamilyBuildUtils.TrySetPartType(famDoc, req.PartType, Warnings);

                // Types first, so parameter values can be set on each.
                var typeNames = (req.Types ?? new List<FamilyTypeValues>()).Select(x => x.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                foreach (var n in typeNames) FamilyBuildUtils.GetOrCreateType(famDoc, n);
                if (mgr.Types.Size == 0) mgr.NewType(req.Name.Trim());

                var formulas = new List<(FamilyParameter p, string f)>();
                foreach (var pi in req.Parameters ?? new List<FamilyParameterInfo>())
                {
                    if (string.IsNullOrWhiteSpace(pi.Name)) continue;
                    var fp = FamilyBuildUtils.GetOrAddParameter(famDoc, pi, Warnings);
                    if (fp == null) continue;
                    if (!string.IsNullOrWhiteSpace(pi.Formula)) { formulas.Add((fp, pi.Formula)); continue; }
                    if (pi.Value == null) continue;
                    foreach (FamilyType ft in mgr.Types)
                    {
                        mgr.CurrentType = ft;
                        if (!FamilyBuildUtils.SetValue(famDoc, fp, pi.Value, out string err)) Warnings.Add(err);
                    }
                }

                Element firstSolid = null;
                foreach (var s in req.Solids ?? new List<FamilySolidInfo>())
                {
                    try
                    {
                        var ext = FamilyBuildUtils.CreateSolid(famDoc, s);
                        firstSolid = firstSolid ?? ext;
                    }
                    catch (Exception ex) { Warnings.Add($"Solid ({s.Shape}): {ex.Message}"); }
                }
                int symbolCurves = FamilyBuildUtils.DrawPlanSymbol(famDoc, req.PlanSymbol, Warnings);

                if (req.Connector != null)
                {
                    famDoc.Regenerate();
                    if (firstSolid == null) Warnings.Add("A connector needs a solid to sit on; add one to solids.");
                    else if (FamilyBuildUtils.AddElectricalConnector(famDoc, firstSolid, req.Connector, Warnings) != null) result.Connectors = 1;
                }

                foreach (var tv in req.Types ?? new List<FamilyTypeValues>())
                {
                    if (string.IsNullOrWhiteSpace(tv.Name)) continue;
                    var ft = FamilyBuildUtils.GetOrCreateType(famDoc, tv.Name);
                    mgr.CurrentType = ft;
                    foreach (var kv in tv.Values ?? new Dictionary<string, string>())
                    {
                        var fp = mgr.get_Parameter(kv.Key);
                        if (fp == null) { Warnings.Add($"Type '{tv.Name}': no parameter '{kv.Key}' (declare it in parameters)."); continue; }
                        if (!FamilyBuildUtils.SetValue(famDoc, fp, kv.Value, out string err)) Warnings.Add($"Type '{tv.Name}': {err}");
                    }
                }

                foreach (var (p, f) in formulas)
                {
                    try { mgr.SetFormula(p, f); } catch (Exception ex) { Warnings.Add($"Formula for '{p.Definition.Name}' ({f}): {ex.Message}"); }
                }

                t.Commit();
                Warnings.AddRange(pre.Messages.Take(10));
                if (pre.RolledBack) return Fail("Revit rolled the family back: " + string.Join("; ", pre.Messages));
                if (symbolCurves > 0) Warnings.Add($"Plan symbol: {symbolCurves} symbolic curve(s).");

                result.Category = famDoc.OwnerFamily.FamilyCategory?.Name;
                result.Parameters = FamilyBuildUtils.ReadParameters(famDoc);
                famDoc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
            }
            finally
            {
                famDoc.Close(false);
            }

            if (req.LoadIntoProject)
            {
                using (var t = new Transaction(Doc, "MCP: load created family"))
                {
                    t.Start();
                    var pre = ElectricalUtils.MakeSilent(t);
                    Family family = null;
                    bool loaded = false;
                    try { loaded = Doc.LoadFamily(path, new FamilyBuildUtils.OverwriteFamilyLoadOptions(), out family); }
                    catch (Exception ex) { Warnings.Add($"Saved but not loaded: {ex.Message}"); }
                    if (family == null)
                        family = new FilteredElementCollector(Doc).OfClass(typeof(Family)).Cast<Family>()
                            .FirstOrDefault(f => DocumentationUtils.SameText(f.Name, result.FamilyName));
                    if (family != null)
                    {
                        result.FamilyId = family.Id.GetValue();
                        result.FamilyName = family.Name;
                        try { result.Placement = family.FamilyPlacementType.ToString(); } catch { }
                        foreach (ElementId sid in family.GetFamilySymbolIds())
                            if (Doc.GetElement(sid) is FamilySymbol fs) result.ProjectTypes.Add(ElectricalUtils.ToTypeInfo(fs));
                    }
                    else if (!loaded) Warnings.Add("Revit did not load the family into the project.");
                    t.Commit();
                }
            }

            return Ok(result, $"Created family '{result.FamilyName}' ({result.Category}) from {result.Template}: {result.Parameters.Count} parameter(s), " +
                $"{result.Connectors} connector(s), saved to {path}" + (result.ProjectTypes.Count > 0 ? $", loaded with {result.ProjectTypes.Count} type(s)." : "."));
        }
    }
}
