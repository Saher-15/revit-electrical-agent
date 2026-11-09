using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>Read-only: everything Claude needs to name things when drawing (views, sheets, title blocks, tags, styles).</summary>
    public class GetDocumentationInfoEventHandler : ElectricalHandlerBase<DocumentationInfoRequest, DocumentationInfo>
    {
        public override string GetName() => "get_documentation_info";

        protected override AIResult<DocumentationInfo> Run()
        {
            var req = Input ?? new DocumentationInfoRequest();
            var info = new DocumentationInfo();

            info.Levels = new FilteredElementCollector(Doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new DocLevelInfo { Id = l.Id.GetValue(), Name = l.Name, ElevationMm = Math.Round(ElectricalUtils.FtToMm(l.Elevation)) })
                .ToList();

            var sheets = new FilteredElementCollector(Doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().OrderBy(s => s.SheetNumber).ToList();
            var sheetOfView = new Dictionary<long, string>();
            var titleBlocksBySheet = new FilteredElementCollector(Doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType().Cast<FamilyInstance>()
                .GroupBy(f => f.OwnerViewId.GetValue()).ToDictionary(g => g.Key, g => g.First());
            foreach (var s in sheets)
            {
                var si = new SheetInfo { Id = s.Id.GetValue(), Number = s.SheetNumber, Name = s.Name };
                if (titleBlocksBySheet.TryGetValue(si.Id, out var tb))
                {
                    si.TitleBlock = $"{tb.Symbol.FamilyName}: {tb.Symbol.Name}";
                    var bb = tb.get_BoundingBox(s);
                    if (bb != null)
                        si.SizeMm = $"{Math.Round(ElectricalUtils.FtToMm(bb.Max.X - bb.Min.X))} x {Math.Round(ElectricalUtils.FtToMm(bb.Max.Y - bb.Min.Y))}";
                }
                foreach (ElementId vid in s.GetAllPlacedViews())
                {
                    if (Doc.GetElement(vid) is View pv)
                    {
                        si.Views.Add(new NamedId { Id = pv.Id.GetValue(), Name = pv.Name, Kind = pv.ViewType.ToString() });
                        sheetOfView[pv.Id.GetValue()] = s.SheetNumber;
                    }
                }
                foreach (var ssi in new FilteredElementCollector(Doc, s.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                {
                    if (ssi.IsTitleblockRevisionSchedule) continue;
                    if (Doc.GetElement(ssi.ScheduleId) is View sv) si.Views.Add(new NamedId { Id = sv.Id.GetValue(), Name = sv.Name, Kind = "Schedule" });
                }
                info.Sheets.Add(si);
            }

            var drawable = new[] { ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.EngineeringPlan, ViewType.AreaPlan, ViewType.Section,
                ViewType.Elevation, ViewType.DraftingView, ViewType.Legend, ViewType.ThreeD, ViewType.Detail };
            var views = new FilteredElementCollector(Doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && drawable.Contains(v.ViewType))
                .Where(v => string.IsNullOrWhiteSpace(req.LevelName) || DocumentationUtils.Contains(v.GenLevel?.Name, req.LevelName))
                .OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name);
            foreach (var v in views) info.Views.Add(DocumentationUtils.ToViewInfo(Doc, v, sheetOfView));

            var active = UiApp.ActiveUIDocument.ActiveView;
            if (active != null) info.ActiveView = DocumentationUtils.ToViewInfo(Doc, active, sheetOfView);

            info.ViewTemplates = new FilteredElementCollector(Doc).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate)
                .OrderBy(v => v.Name).Select(v => new NamedId { Id = v.Id.GetValue(), Name = v.Name, Kind = v.ViewType.ToString() }).ToList();
            info.ViewFamilyTypes = new FilteredElementCollector(Doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(t => t.ViewFamily == ViewFamily.FloorPlan || t.ViewFamily == ViewFamily.CeilingPlan || t.ViewFamily == ViewFamily.Drafting)
                .Select(t => new NamedId { Id = t.Id.GetValue(), Name = t.Name, Kind = t.ViewFamily.ToString() }).ToList();

            info.TitleBlocks = new FilteredElementCollector(Doc).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>().Select(s => new TypeRef { Id = s.Id.GetValue(), FamilyName = s.FamilyName, TypeName = s.Name }).ToList();

            var tagCats = new HashSet<BuiltInCategory>(DocumentationUtils.TagCategoryOf.Values);
            foreach (var s in new FilteredElementCollector(Doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
            {
                var cat = s.Category;
                if (cat == null) continue;
                var bic = (BuiltInCategory)cat.Id.GetValue();
                if (!tagCats.Contains(bic)) continue;
                info.TagTypes.Add(new TypeRef { Id = s.Id.GetValue(), Category = bic.ToString(), FamilyName = s.FamilyName, TypeName = s.Name });
            }
            info.TagTypes = info.TagTypes.OrderBy(t => t.Category).ThenBy(t => t.FamilyName).ToList();

            info.TextNoteTypes = new FilteredElementCollector(Doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                .Select(t => new NamedId { Id = t.Id.GetValue(), Name = t.Name }).ToList();
            info.LineStyles = DocumentationUtils.LineStyleNames(Doc);
            info.WireTypes = new FilteredElementCollector(Doc).OfClass(typeof(WireType)).Cast<WireType>()
                .Select(t => new NamedId { Id = t.Id.GetValue(), Name = t.Name }).ToList();
            info.Schedules = new FilteredElementCollector(Doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule)
                .Select(s => new NamedId { Id = s.Id.GetValue(), Name = s.Name }).ToList();
            info.Schedules.AddRange(new FilteredElementCollector(Doc).OfClass(typeof(PanelScheduleView)).Cast<PanelScheduleView>()
                .Where(s => !s.IsTemplate).Select(s => new NamedId { Id = s.Id.GetValue(), Name = s.Name, Kind = "PanelSchedule" }));
            info.PanelScheduleTemplates = new FilteredElementCollector(Doc).OfClass(typeof(PanelScheduleTemplate)).Cast<PanelScheduleTemplate>()
                .Select(t => new NamedId { Id = t.Id.GetValue(), Name = t.Name, Kind = t.GetPanelScheduleType().ToString() }).ToList();
            info.Panels = new FilteredElementCollector(Doc).OfCategory(BuiltInCategory.OST_ElectricalEquipment).WhereElementIsNotElementType()
                .Cast<Element>().Select(p => new NamedId
                {
                    Id = p.Id.GetValue(),
                    Name = p.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString() ?? p.Name,
                    Kind = (p as FamilyInstance)?.Symbol?.FamilyName,
                }).OrderBy(p => p.Name).ToList();

            return Ok(info, $"{info.Views.Count} views, {info.Sheets.Count} sheets, {info.TitleBlocks.Count} title block types, {info.TagTypes.Count} tag types, {info.Panels.Count} panels.");
        }
    }
}
