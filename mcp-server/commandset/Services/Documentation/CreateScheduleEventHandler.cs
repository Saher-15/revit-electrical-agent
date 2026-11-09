using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>Creates a schedule (quantities / lists) from a preset or explicit fields, optionally placed on a sheet.</summary>
    public class CreateScheduleEventHandler : ElectricalHandlerBase<ScheduleRequest, ScheduleResult>
    {
        public override string GetName() => "create_schedule";

        private class Preset
        {
            public BuiltInCategory Category;
            public string[] Fields;
            public string[] Sort;
            public bool Itemize;
            public string[] Totals = new string[0];
            public string Name;
        }

        private static readonly Dictionary<string, Preset> Presets = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase)
        {
            { "lighting", new Preset { Name = "Lighting Fixture Schedule", Category = BuiltInCategory.OST_LightingFixtures,
                Fields = new[] { "Level", "Family and Type", "Type Mark", "Count" }, Sort = new[] { "Level", "Family and Type" } } },
            { "devices", new Preset { Name = "Electrical Device Schedule", Category = BuiltInCategory.OST_ElectricalFixtures,
                Fields = new[] { "Level", "Family and Type", "Type Mark", "Count" }, Sort = new[] { "Level", "Family and Type" } } },
            { "switches", new Preset { Name = "Lighting Device Schedule", Category = BuiltInCategory.OST_LightingDevices,
                Fields = new[] { "Level", "Family and Type", "Type Mark", "Count" }, Sort = new[] { "Level", "Family and Type" } } },
            { "panels", new Preset { Name = "Panel Schedule List", Category = BuiltInCategory.OST_ElectricalEquipment, Itemize = true,
                Fields = new[] { "Panel Name", "Family and Type", "Level", "Supply From", "Mains", "Total Connected Load", "Total Demand Load" },
                Sort = new[] { "Panel Name" } } },
            { "circuits", new Preset { Name = "Circuit Schedule", Category = BuiltInCategory.OST_ElectricalCircuit, Itemize = true,
                Fields = new[] { "Panel", "Circuit Number", "Load Name", "Rating", "Number of Poles", "Apparent Load", "Wire Size", "Length" },
                Sort = new[] { "Panel", "Circuit Number" } } },
            { "conduit", new Preset { Name = "Conduit Quantities", Category = BuiltInCategory.OST_Conduit,
                Fields = new[] { "Type", "Diameter(Trade Size)", "Length" }, Sort = new[] { "Type", "Diameter(Trade Size)" }, Totals = new[] { "Length" } } },
            { "cableTray", new Preset { Name = "Cable Tray Quantities", Category = BuiltInCategory.OST_CableTray,
                Fields = new[] { "Type", "Width", "Height", "Length" }, Sort = new[] { "Type", "Width", "Height" }, Totals = new[] { "Length" } } },
        };

        protected override AIResult<ScheduleResult> Run()
        {
            var req = Input ?? new ScheduleRequest();
            Preset preset = null;
            if (!string.IsNullOrWhiteSpace(req.Preset) && !Presets.TryGetValue(req.Preset.Trim(), out preset))
                return Fail($"Unknown preset '{req.Preset}'. Presets: {string.Join(", ", Presets.Keys)}");

            BuiltInCategory bic;
            if (!string.IsNullOrWhiteSpace(req.Category))
            {
                if (!ElectricalUtils.TryParseCategory(req.Category, out bic)) return Fail($"Unknown category '{req.Category}'.");
            }
            else if (preset != null) bic = preset.Category;
            else return Fail("Give preset or category.");

            var fieldNames = req.Fields != null && req.Fields.Count > 0 ? req.Fields : preset?.Fields.ToList() ?? new List<string>();
            var sorts = req.SortBy != null && req.SortBy.Count > 0
                ? req.SortBy
                : (preset?.Sort ?? new string[0]).Select((f, i) => new ScheduleSortInfo { Field = f, Header = i == 0 && preset != null && !preset.Itemize }).ToList();
            var totals = req.Totals != null && req.Totals.Count > 0 ? req.Totals : preset?.Totals.ToList() ?? new List<string>();
            bool itemize = req.Itemize ?? preset?.Itemize ?? true;
            var result = new ScheduleResult { Category = bic.ToString() };

            using (var t = new Transaction(Doc, "MCP: create schedule"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);

                ViewSchedule schedule = ViewSchedule.CreateSchedule(Doc, new ElementId(bic));
                string name = string.IsNullOrWhiteSpace(req.Name) ? preset?.Name ?? $"{bic.ToString().Replace("OST_", "")} Schedule" : req.Name.Trim();
                schedule.Name = DocumentationUtils.UniqueViewName(Doc, name);

                var def = schedule.Definition;
                var schedulable = def.GetSchedulableFields().ToList();
                var added = new Dictionary<string, ScheduleField>(StringComparer.OrdinalIgnoreCase);
                var missing = new List<string>();
                foreach (var fname in fieldNames)
                {
                    var sf = DocumentationUtils.PickByName(schedulable, f => f.GetName(Doc), fname);
                    if (sf == null) { missing.Add(fname); continue; }
                    var field = def.AddField(sf);
                    added[fname] = field;
                    result.Fields.Add(field.GetName());
                }
                if (added.Count == 0)
                {
                    t.RollBack();
                    result.AvailableFields = schedulable.Select(f => f.GetName(Doc)).Distinct().OrderBy(n => n).ToList();
                    return Fail($"None of the fields exist for {bic}. See availableFields in the message: " + string.Join(", ", result.AvailableFields.Take(80)));
                }
                if (missing.Count > 0)
                {
                    Warnings.Add($"Fields not found: {string.Join(", ", missing)}.");
                    result.AvailableFields = schedulable.Select(f => f.GetName(Doc)).Distinct().OrderBy(n => n).ToList();
                }

                foreach (var f in req.Filters ?? new List<ScheduleFilterInfo>())
                {
                    if (!added.TryGetValue(f.Field ?? "", out var field))
                    {
                        Warnings.Add($"Filter field '{f.Field}' must also be in fields; filter skipped.");
                        continue;
                    }
                    AddFilter(def, field, f);
                }

                foreach (var s in sorts)
                {
                    if (!added.TryGetValue(s.Field ?? "", out var field)) { Warnings.Add($"Sort field '{s.Field}' is not in fields; skipped."); continue; }
                    var sg = new ScheduleSortGroupField(field.FieldId, s.Descending ? ScheduleSortOrder.Descending : ScheduleSortOrder.Ascending)
                    {
                        ShowHeader = s.Header,
                        ShowFooter = s.Footer,
                    };
                    def.AddSortGroupField(sg);
                }

                foreach (var name2 in totals)
                {
                    if (!added.TryGetValue(name2, out var field)) continue;
                    try { field.DisplayType = ScheduleFieldDisplayType.Totals; } catch (Exception ex) { Warnings.Add($"Totals on '{name2}': {ex.Message}"); }
                }

                def.IsItemized = itemize;
                if (req.GrandTotal)
                {
                    def.ShowGrandTotal = true;
                    try { def.ShowGrandTotalCount = true; } catch { }
                }

                if (req.SheetId > 0)
                {
                    if (Doc.GetElement(ElectricalUtils.ToElementId(req.SheetId)) is ViewSheet sheet)
                    {
                        XYZ origin = new XYZ(ElectricalUtils.MmToFt(req.X ?? 30), ElectricalUtils.MmToFt(req.Y ?? 570), 0);
                        var ssi = ScheduleSheetInstance.Create(Doc, sheet.Id, schedule.Id, origin);
                        result.SheetInstanceId = ssi.Id.GetValue();
                    }
                    else Warnings.Add($"sheetId {req.SheetId} is not a sheet; schedule not placed.");
                }

                t.Commit();
                Warnings.AddRange(pre.Messages.Take(10));
                if (pre.RolledBack) return Fail("Revit rolled the schedule back.");

                result.ScheduleId = schedule.Id.GetValue();
                result.Name = schedule.Name;
                try { result.Rows = schedule.GetTableData().GetSectionData(SectionType.Body).NumberOfRows; } catch { }
            }

            return Ok(result, $"Created schedule '{result.Name}' with {result.Fields.Count} field(s) and {result.Rows} row(s).");
        }

        private void AddFilter(ScheduleDefinition def, ScheduleField field, ScheduleFilterInfo f)
        {
            ScheduleFilterType type;
            switch ((f.Condition ?? "equal").Replace(" ", "").ToLowerInvariant())
            {
                case "equal": case "equals": case "=": type = ScheduleFilterType.Equal; break;
                case "notequal": case "!=": type = ScheduleFilterType.NotEqual; break;
                case "contains": type = ScheduleFilterType.Contains; break;
                case "notcontains": type = ScheduleFilterType.NotContains; break;
                case "beginswith": type = ScheduleFilterType.BeginsWith; break;
                case "endswith": type = ScheduleFilterType.EndsWith; break;
                case "greater": case ">": type = ScheduleFilterType.GreaterThan; break;
                case "greaterorequal": case ">=": type = ScheduleFilterType.GreaterThanOrEqual; break;
                case "less": case "<": type = ScheduleFilterType.LessThan; break;
                case "lessorequal": case "<=": type = ScheduleFilterType.LessThanOrEqual; break;
                case "hasvalue": type = ScheduleFilterType.HasValue; break;
                case "hasnovalue": type = ScheduleFilterType.HasNoValue; break;
                default: Warnings.Add($"Unknown filter condition '{f.Condition}'."); return;
            }
            try
            {
                if (type == ScheduleFilterType.HasValue || type == ScheduleFilterType.HasNoValue)
                {
                    def.AddFilter(new ScheduleFilter(field.FieldId, type));
                    return;
                }
                string value = f.Value ?? "";
                var spec = field.GetSpecTypeId();
                bool numeric = spec != null && UnitUtils.IsMeasurableSpec(spec);
                if (numeric && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                {
                    if (spec == SpecTypeId.Length) d = ElectricalUtils.MmToFt(d);
                    else
                    {
                        var fo = Doc.GetUnits().GetFormatOptions(spec);
                        d = UnitUtils.ConvertToInternalUnits(d, fo.GetUnitTypeId());
                    }
                    def.AddFilter(new ScheduleFilter(field.FieldId, type, d));
                    return;
                }
                if (DocumentationUtils.SameText(field.GetName(), "Level"))
                {
                    var level = DocumentationUtils.PickByName(new FilteredElementCollector(Doc).OfClass(typeof(Level)).Cast<Level>(), l => l.Name, value);
                    if (level != null) { def.AddFilter(new ScheduleFilter(field.FieldId, type, level.Id)); return; }
                }
                if (int.TryParse(value, out int i) && spec != null && (spec == SpecTypeId.Int.Integer || spec == SpecTypeId.Boolean.YesNo))
                {
                    def.AddFilter(new ScheduleFilter(field.FieldId, type, i));
                    return;
                }
                def.AddFilter(new ScheduleFilter(field.FieldId, type, value));
            }
            catch (Exception ex)
            {
                Warnings.Add($"Filter on '{f.Field}' {f.Condition} '{f.Value}' not added: {ex.Message}");
            }
        }
    }
}
