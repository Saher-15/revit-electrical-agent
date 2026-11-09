using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>Duplicates a project type under a new name and/or sets its type parameters (no family editing needed).</summary>
    public class SetFamilyTypeEventHandler : ElectricalHandlerBase<List<FamilyTypeRequest>, List<FamilyTypeResult>>
    {
        public override string GetName() => "set_family_type";

        protected override AIResult<List<FamilyTypeResult>> Run()
        {
            var requests = Input ?? new List<FamilyTypeRequest>();
            if (requests.Count == 0) return Fail("No types requested.");
            var results = new List<FamilyTypeResult>();

            using (var t = new Transaction(Doc, "MCP: family types"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);
                foreach (var req in requests)
                {
                    var r = new FamilyTypeResult();
                    results.Add(r);
                    ElementType type;
                    if (req.TypeId > 0 && Doc.GetElement(ElectricalUtils.ToElementId(req.TypeId)) is ElementType byId) type = byId;
                    else
                    {
                        type = ElectricalUtils.ResolveSymbol(Doc, -1, req.Category, req.FamilyName, req.TypeName, out string err);
                        if (type == null) { r.Errors.Add(err); continue; }
                    }

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(req.NewName))
                        {
                            string familyName = (type as FamilySymbol)?.FamilyName;
                            var existing = new FilteredElementCollector(Doc).OfClass(type.GetType()).Cast<ElementType>()
                                .FirstOrDefault(x => DocumentationUtils.SameText(x.Name, req.NewName)
                                    && (familyName == null || DocumentationUtils.SameText((x as FamilySymbol)?.FamilyName, familyName)));
                            if (existing != null)
                            {
                                r.Errors.Add($"Type '{req.NewName}' already exists; updated it instead of duplicating.");
                                type = existing;
                            }
                            else
                            {
                                type = type.Duplicate(req.NewName.Trim());
                                r.Created = true;
                            }
                        }
                        foreach (var kv in req.Values ?? new Dictionary<string, string>())
                        {
                            if (!ElectricalUtils.TrySetParameter(type, kv.Key, kv.Value, out string err))
                                r.Errors.Add(err + (type.LookupParameter(kv.Key) == null ? " (it may be an instance parameter, or exist only inside the family: use edit_family)" : ""));
                        }
                        r.TypeId = type.Id.GetValue();
                        r.TypeName = type.Name;
                        r.FamilyName = (type as FamilySymbol)?.FamilyName ?? type.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString();
                    }
                    catch (Exception ex) { r.Errors.Add(ex.Message); }
                }
                t.Commit();
                Warnings.AddRange(pre.Messages.Take(10));
                if (pre.RolledBack) return Fail("Revit rolled the type changes back.");
            }

            int ok = results.Count(r => r.TypeId > 0);
            if (ok == 0) return Fail("No type changed: " + string.Join("; ", results.SelectMany(r => r.Errors)));
            return Ok(results, $"{results.Count(r => r.Created)} type(s) created, {ok} type(s) updated.");
        }
    }
}
