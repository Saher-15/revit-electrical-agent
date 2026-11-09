using System.IO;
using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Opens a loaded family, reports its parameters/types/connectors and optionally changes them
    /// (parameters, types, formulas, connector values, part type), then reloads it into the project.
    /// With no changes requested it is read-only.
    /// </summary>
    public class EditFamilyEventHandler : ElectricalHandlerBase<EditFamilyRequest, FamilyResult>
    {
        public override string GetName() => "edit_family";

        protected override AIResult<FamilyResult> Run()
        {
            var req = Input ?? new EditFamilyRequest();
            var families = new FilteredElementCollector(Doc).OfClass(typeof(Family)).Cast<Family>().ToList();
            Family family = req.FamilyId > 0
                ? families.FirstOrDefault(f => f.Id.GetValue() == req.FamilyId)
                : DocumentationUtils.PickByName(families, f => f.Name, req.FamilyName);
            if (family == null) return Fail($"Family '{req.FamilyName ?? req.FamilyId.ToString()}' is not loaded in the project.");
            if (!family.IsEditable) return Fail($"'{family.Name}' cannot be edited (system or in-place family).");

            bool changes = (req.AddParameters?.Count ?? 0) > 0 || (req.Types?.Count ?? 0) > 0 || (req.DeleteTypes?.Count ?? 0) > 0
                || (req.Formulas?.Count ?? 0) > 0 || req.Connector != null || !string.IsNullOrWhiteSpace(req.PartType);
            var result = new FamilyResult { FamilyName = family.Name, FamilyId = family.Id.GetValue(), Category = family.FamilyCategory?.Name };
            try { result.Placement = family.FamilyPlacementType.ToString(); } catch { }

            Document famDoc = Doc.EditFamily(family);
            if (famDoc == null) return Fail($"Revit could not open '{family.Name}' for editing.");
            try
            {
                var mgr = famDoc.FamilyManager;
                if (changes)
                {
                    var t = FamilyBuildUtils.StartSilent(famDoc, "MCP: edit family", out var pre);

                    FamilyBuildUtils.TrySetPartType(famDoc, req.PartType, Warnings);

                    foreach (var pi in req.AddParameters ?? new List<FamilyParameterInfo>())
                    {
                        if (string.IsNullOrWhiteSpace(pi.Name)) continue;
                        if (mgr.get_Parameter(pi.Name) != null) Warnings.Add($"Parameter '{pi.Name}' already exists; values only.");
                        var fp = FamilyBuildUtils.GetOrAddParameter(famDoc, pi, Warnings);
                        if (fp == null) continue;
                        if (!string.IsNullOrWhiteSpace(pi.Formula))
                        {
                            try { mgr.SetFormula(fp, pi.Formula); } catch (Exception ex) { Warnings.Add($"Formula '{pi.Formula}': {ex.Message}"); }
                            continue;
                        }
                        if (pi.Value == null) continue;
                        foreach (FamilyType ft in mgr.Types)
                        {
                            mgr.CurrentType = ft;
                            if (!FamilyBuildUtils.SetValue(famDoc, fp, pi.Value, out string err)) Warnings.Add(err);
                        }
                    }

                    foreach (var tv in req.Types ?? new List<FamilyTypeValues>())
                    {
                        if (string.IsNullOrWhiteSpace(tv.Name)) continue;
                        var ft = FamilyBuildUtils.GetOrCreateType(famDoc, tv.Name);
                        mgr.CurrentType = ft;
                        if (!string.IsNullOrWhiteSpace(tv.NewName))
                        {
                            try { mgr.RenameCurrentType(tv.NewName.Trim()); } catch (Exception ex) { Warnings.Add($"Rename '{tv.Name}': {ex.Message}"); }
                        }
                        foreach (var kv in tv.Values ?? new Dictionary<string, string>())
                        {
                            var fp = mgr.get_Parameter(kv.Key);
                            if (fp == null) { Warnings.Add($"Type '{tv.Name}': no parameter '{kv.Key}'."); continue; }
                            if (!FamilyBuildUtils.SetValue(famDoc, fp, kv.Value, out string err)) Warnings.Add($"Type '{tv.Name}': {err}");
                        }
                    }

                    foreach (var name in req.DeleteTypes ?? new List<string>())
                    {
                        FamilyType target = null;
                        foreach (FamilyType ft in mgr.Types) if (DocumentationUtils.SameText(ft.Name, name)) target = ft;
                        if (target == null) { Warnings.Add($"Type '{name}' not found."); continue; }
                        if (mgr.Types.Size <= 1) { Warnings.Add($"'{name}' is the last type; not deleted."); continue; }
                        mgr.CurrentType = target;
                        try { mgr.DeleteCurrentType(); } catch (Exception ex) { Warnings.Add($"Delete '{name}': {ex.Message}"); }
                    }

                    foreach (var kv in req.Formulas ?? new Dictionary<string, string>())
                    {
                        var fp = mgr.get_Parameter(kv.Key);
                        if (fp == null) { Warnings.Add($"Formula: no parameter '{kv.Key}'."); continue; }
                        try { mgr.SetFormula(fp, string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value); }
                        catch (Exception ex) { Warnings.Add($"Formula for '{kv.Key}': {ex.Message}"); }
                    }

                    if (req.Connector != null)
                    {
                        var conn = new FilteredElementCollector(famDoc).OfClass(typeof(ConnectorElement)).Cast<ConnectorElement>()
                            .FirstOrDefault(c => c.Domain == Domain.DomainElectrical);
                        if (conn != null) FamilyBuildUtils.ApplyConnectorValues(famDoc, conn, req.Connector, Warnings);
                        else
                        {
                            var solid = new FilteredElementCollector(famDoc).OfClass(typeof(GenericForm)).FirstElement();
                            if (solid == null) Warnings.Add("The family has no solid to put a connector on.");
                            else FamilyBuildUtils.AddElectricalConnector(famDoc, solid, req.Connector, Warnings);
                        }
                    }

                    t.Commit();
                    Warnings.AddRange(pre.Messages.Take(10));
                    if (pre.RolledBack) return Fail("Revit rolled the family changes back: " + string.Join("; ", pre.Messages));
                }

                result.Parameters = FamilyBuildUtils.ReadParameters(famDoc);
                result.Connectors = new FilteredElementCollector(famDoc).OfClass(typeof(ConnectorElement)).GetElementCount();

                if (!string.IsNullOrWhiteSpace(req.SaveFolder))
                {
                    Directory.CreateDirectory(req.SaveFolder);
                    string path = Path.Combine(req.SaveFolder, DocumentationUtils.SafeFileName(family.Name) + ".rfa");
                    famDoc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
                    result.FilePath = path;
                }

                if (changes)
                {
                    var reloaded = famDoc.LoadFamily(Doc, new FamilyBuildUtils.OverwriteFamilyLoadOptions());
                    if (reloaded != null) family = reloaded;
                }
            }
            finally
            {
                famDoc.Close(false);
            }

            family = Doc.GetElement(ElectricalUtils.ToElementId(result.FamilyId)) as Family ?? family;
            foreach (ElementId sid in family.GetFamilySymbolIds())
                if (Doc.GetElement(sid) is FamilySymbol fs) result.ProjectTypes.Add(ElectricalUtils.ToTypeInfo(fs));

            return Ok(result, changes
                ? $"Edited and reloaded '{result.FamilyName}': {result.Parameters.Count} parameter(s), {result.ProjectTypes.Count} type(s)."
                : $"'{result.FamilyName}': {result.Parameters.Count} parameter(s), {result.ProjectTypes.Count} type(s), {result.Connectors} connector(s). No changes requested.");
        }
    }
}
