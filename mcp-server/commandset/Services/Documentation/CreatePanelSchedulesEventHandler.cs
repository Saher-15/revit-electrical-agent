using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>Creates panel schedules for panels (reusing existing ones) and optionally lays them out on a sheet.</summary>
    public class CreatePanelSchedulesEventHandler : ElectricalHandlerBase<PanelScheduleRequest, PanelScheduleResult>
    {
        public override string GetName() => "create_panel_schedules";

        protected override AIResult<PanelScheduleResult> Run()
        {
            var req = Input ?? new PanelScheduleRequest();
            var result = new PanelScheduleResult();

            var panels = new FilteredElementCollector(Doc).OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            string PanelName(Element p) => p.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString() ?? p.Name;

            var selected = new List<FamilyInstance>();
            if (req.All) selected.AddRange(panels);
            foreach (long id in req.PanelIds ?? new List<long>())
            {
                var p = panels.FirstOrDefault(x => x.Id.GetValue() == id);
                if (p != null) selected.Add(p); else result.Errors.Add($"{id} is not an electrical equipment instance.");
            }
            foreach (var n in req.PanelNames ?? new List<string>())
            {
                var p = DocumentationUtils.PickByName(panels, PanelName, n);
                if (p != null) selected.Add(p); else result.Errors.Add($"Panel '{n}' not found.");
            }
            selected = selected.GroupBy(p => p.Id.GetValue()).Select(g => g.First()).OrderBy(PanelName).ToList();
            if (selected.Count == 0) return Fail("No panel selected. Give panelIds, panelNames or all=true. " + string.Join(" ", result.Errors));

            var existing = new FilteredElementCollector(Doc).OfClass(typeof(PanelScheduleView)).Cast<PanelScheduleView>()
                .Where(v => !v.IsTemplate).ToList();
            var templates = new FilteredElementCollector(Doc).OfClass(typeof(PanelScheduleTemplate)).Cast<PanelScheduleTemplate>().ToList();
            var template = DocumentationUtils.PickByName(templates, t => t.Name, req.TemplateName);
            if (!string.IsNullOrWhiteSpace(req.TemplateName) && template == null)
                Warnings.Add($"Panel schedule template '{req.TemplateName}' not found; Revit default used. Templates: {string.Join(", ", templates.Select(t => t.Name))}");

            ViewSheet sheet = null;
            if (req.SheetId > 0)
            {
                sheet = Doc.GetElement(ElectricalUtils.ToElementId(req.SheetId)) as ViewSheet;
                if (sheet == null) Warnings.Add($"sheetId {req.SheetId} is not a sheet; schedules not placed.");
            }

            using (var t = new Transaction(Doc, "MCP: panel schedules"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);
                double x = ElectricalUtils.MmToFt(req.X ?? 30), y = ElectricalUtils.MmToFt(req.Y ?? 570);
                foreach (var panel in selected)
                {
                    try
                    {
                        var view = existing.FirstOrDefault(v => v.GetPanel() == panel.Id);
                        string kind = "existing";
                        if (view == null)
                        {
                            view = template != null
                                ? PanelScheduleView.CreateInstanceView(Doc, template.Id, panel.Id)
                                : PanelScheduleView.CreateInstanceView(Doc, panel.Id);
                            kind = "created";
                        }
                        if (view == null) { result.Errors.Add($"{PanelName(panel)}: Revit did not create a panel schedule (is the panel's Part Type a panelboard/switchboard?)."); continue; }
                        result.Schedules.Add(new NamedId { Id = view.Id.GetValue(), Name = view.Name, Kind = kind });

                        if (sheet != null)
                        {
                            var inst = PanelScheduleSheetInstance.Create(Doc, view.Id, sheet);
                            inst.Origin = new XYZ(x, y, 0);
                            Doc.Regenerate();
                            var bb = inst.get_BoundingBox(sheet);
                            x = bb != null ? bb.Max.X + ElectricalUtils.MmToFt(10) : x + ElectricalUtils.MmToFt(200);
                            result.PlacedOnSheet++;
                        }
                    }
                    catch (Exception ex) { result.Errors.Add($"{PanelName(panel)}: {ex.Message}"); }
                }
                t.Commit();
                Warnings.AddRange(pre.Messages.Take(10));
                if (pre.RolledBack) return Fail("Revit rolled the panel schedules back.");
            }

            if (result.Schedules.Count == 0) return Fail("No panel schedule created. " + string.Join(" ", result.Errors));
            return Ok(result, $"{result.Schedules.Count} panel schedule(s) ({result.Schedules.Count(s => s.Kind == "created")} new), {result.PlacedOnSheet} placed on the sheet.");
        }
    }
}
