using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>Creates sheets with a title block and places views, schedules and panel schedules on them.</summary>
    public class CreateSheetsEventHandler : ElectricalHandlerBase<List<SheetRequest>, List<CreatedSheet>>
    {
        public override string GetName() => "create_sheets";

        private const double MarginMm = 25;

        protected override AIResult<List<CreatedSheet>> Run()
        {
            var requests = Input ?? new List<SheetRequest>();
            if (requests.Count == 0) return Fail("No sheets requested.");
            var results = new List<CreatedSheet>();

            using (var t = new Transaction(Doc, "MCP: create sheets"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);
                foreach (var req in requests)
                {
                    var r = new CreatedSheet { Number = req.Number, Name = req.Name };
                    try { Process(req, r); }
                    catch (Exception ex) { r.Errors.Add(ex.Message); }
                    results.Add(r);
                }
                t.Commit();
                Warnings.AddRange(pre.Messages);
                if (pre.RolledBack) return Fail("Revit rolled the sheets back.");
            }

            int ok = results.Count(r => r.SheetId > 0);
            if (ok == 0) return Fail("No sheet was created: " + string.Join("; ", results.SelectMany(r => r.Errors)));
            return Ok(results, $"{ok} sheet(s), {results.Sum(r => r.Placed.Count)} view(s)/schedule(s) placed.");
        }

        private void Process(SheetRequest req, CreatedSheet r)
        {
            ViewSheet sheet;
            if (req.SheetId > 0)
            {
                sheet = Doc.GetElement(ElectricalUtils.ToElementId(req.SheetId)) as ViewSheet;
                if (sheet == null) { r.Errors.Add($"sheetId {req.SheetId} is not a sheet."); return; }
            }
            else
            {
                ElementId titleBlockId = ResolveTitleBlock(req, r);
                sheet = ViewSheet.Create(Doc, titleBlockId);
                if (!string.IsNullOrWhiteSpace(req.Number))
                {
                    bool taken = new FilteredElementCollector(Doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                        .Any(s => s.Id != sheet.Id && DocumentationUtils.SameText(s.SheetNumber, req.Number));
                    if (taken) r.Errors.Add($"Sheet number '{req.Number}' is taken; Revit numbered it '{sheet.SheetNumber}'.");
                    else sheet.SheetNumber = req.Number.Trim();
                }
                if (!string.IsNullOrWhiteSpace(req.Name)) sheet.Name = req.Name.Trim();
            }
            r.SheetId = sheet.Id.GetValue();
            r.Number = sheet.SheetNumber;
            r.Name = sheet.Name;

            foreach (var kv in req.Parameters ?? new Dictionary<string, string>())
            {
                if (!ElectricalUtils.TrySetParameter(sheet, kv.Key, kv.Value, out string err)) r.Errors.Add(err);
            }

            Doc.Regenerate();
            GetUsableArea(sheet, out XYZ min, out XYZ max);

            // Views: explicit positions, otherwise a grid over the usable area.
            var views = (req.Views ?? new List<SheetPlacement>()).ToList();
            int autoCount = views.Count(v => !v.X.HasValue || !v.Y.HasValue);
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(autoCount)));
            int rows = Math.Max(1, (int)Math.Ceiling(autoCount / (double)cols));
            int autoIndex = 0;
            foreach (var p in views)
            {
                View view = ResolveView(p, r);
                if (view == null) continue;
                XYZ center;
                if (p.X.HasValue && p.Y.HasValue)
                    center = new XYZ(ElectricalUtils.MmToFt(p.X.Value), ElectricalUtils.MmToFt(p.Y.Value), 0);
                else
                {
                    int c = autoIndex % cols, row = autoIndex / cols;
                    autoIndex++;
                    double w = (max.X - min.X) / cols, h = (max.Y - min.Y) / rows;
                    center = new XYZ(min.X + w * (c + 0.5), max.Y - h * (row + 0.5), 0);
                }

                // Already on this sheet: move it.
                var existing = new FilteredElementCollector(Doc, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>()
                    .FirstOrDefault(vp => vp.ViewId == view.Id);
                if (existing != null)
                {
                    existing.SetBoxCenter(center);
                    r.Placed.Add(new NamedId { Id = existing.Id.GetValue(), Name = view.Name, Kind = "moved" });
                    continue;
                }
                if (!Viewport.CanAddViewToSheet(Doc, sheet.Id, view.Id))
                {
                    r.Errors.Add($"'{view.Name}' cannot be placed (it is already on another sheet, or is a schedule/template).");
                    continue;
                }
                var vpNew = Viewport.Create(Doc, sheet.Id, view.Id, center);
                if (!string.IsNullOrWhiteSpace(p.ViewportTypeName))
                {
                    var vpType = DocumentationUtils.PickByName(
                        vpNew.GetValidTypes().Select(id => Doc.GetElement(id)).Where(e => e != null), e => e.Name, p.ViewportTypeName);
                    if (vpType != null) vpNew.ChangeTypeId(vpType.Id);
                    else r.Errors.Add($"Viewport type '{p.ViewportTypeName}' not found.");
                }
                r.Placed.Add(new NamedId { Id = vpNew.Id.GetValue(), Name = view.Name, Kind = DescribeFit(vpNew, min, max) });
            }

            // Schedules: explicit top-left, otherwise stacked down the left side.
            double nextTop = max.Y;
            foreach (var p in req.Schedules ?? new List<SheetPlacement>())
            {
                View view = ResolveView(p, r);
                if (view == null) continue;
                XYZ origin = p.X.HasValue && p.Y.HasValue
                    ? new XYZ(ElectricalUtils.MmToFt(p.X.Value), ElectricalUtils.MmToFt(p.Y.Value), 0)
                    : new XYZ(min.X, nextTop, 0);
                Element placed;
                if (view is PanelScheduleView psv)
                {
                    var inst = PanelScheduleSheetInstance.Create(Doc, psv.Id, sheet);
                    inst.Origin = origin;
                    placed = inst;
                }
                else if (view is ViewSchedule vs)
                {
                    placed = ScheduleSheetInstance.Create(Doc, sheet.Id, vs.Id, origin);
                }
                else
                {
                    r.Errors.Add($"'{view.Name}' is not a schedule; put it under views.");
                    continue;
                }
                Doc.Regenerate();
                var bb = placed.get_BoundingBox(sheet);
                if (bb != null) nextTop = bb.Min.Y - ElectricalUtils.MmToFt(5);
                r.Placed.Add(new NamedId { Id = placed.Id.GetValue(), Name = view.Name, Kind = "schedule" });
            }
        }

        private ElementId ResolveTitleBlock(SheetRequest req, CreatedSheet r)
        {
            var titleBlocks = new FilteredElementCollector(Doc).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>().ToList();
            FamilySymbol tb = null;
            if (req.TitleBlockId > 0) tb = titleBlocks.FirstOrDefault(s => s.Id.GetValue() == req.TitleBlockId);
            if (tb == null && !string.IsNullOrWhiteSpace(req.TitleBlockName))
                tb = DocumentationUtils.PickByName(titleBlocks, s => $"{s.FamilyName}: {s.Name}", req.TitleBlockName);
            if (tb == null && (req.TitleBlockId > 0 || !string.IsNullOrWhiteSpace(req.TitleBlockName)))
                r.Errors.Add($"Title block '{req.TitleBlockName ?? req.TitleBlockId.ToString()}' not found; used {(titleBlocks.Count > 0 ? "the first one" : "none")}.");
            tb = tb ?? titleBlocks.FirstOrDefault();
            if (tb == null)
            {
                r.Errors.Add("The project has no title block; the sheet is empty. Load one with load_family.");
                return ElementId.InvalidElementId;
            }
            if (!tb.IsActive) tb.Activate();
            return tb.Id;
        }

        private View ResolveView(SheetPlacement p, CreatedSheet r)
        {
            View view = null;
            if (p.ViewId > 0) view = Doc.GetElement(ElectricalUtils.ToElementId(p.ViewId)) as View;
            if (view == null && !string.IsNullOrWhiteSpace(p.ViewName)) view = DocumentationUtils.FindViewByName(Doc, p.ViewName);
            if (view == null) r.Errors.Add($"View '{p.ViewName ?? p.ViewId.ToString()}' not found.");
            return view;
        }

        /// <summary>Inside of the title block (or an A1 sheet when there is none), minus a margin. Sheet coordinates in feet.</summary>
        private void GetUsableArea(ViewSheet sheet, out XYZ min, out XYZ max)
        {
            var tb = new FilteredElementCollector(Doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstElement();
            var bb = tb?.get_BoundingBox(sheet);
            double m = ElectricalUtils.MmToFt(MarginMm);
            if (bb == null)
            {
                min = new XYZ(m, m, 0);
                max = new XYZ(ElectricalUtils.MmToFt(841) - m, ElectricalUtils.MmToFt(594) - m, 0);
                return;
            }
            min = new XYZ(bb.Min.X + m, bb.Min.Y + m, 0);
            max = new XYZ(bb.Max.X - m, bb.Max.Y - m, 0);
        }

        private string DescribeFit(Viewport vp, XYZ min, XYZ max)
        {
            try
            {
                Doc.Regenerate();
                var o = vp.GetBoxOutline();
                double w = ElectricalUtils.FtToMm(o.MaximumPoint.X - o.MinimumPoint.X);
                double h = ElectricalUtils.FtToMm(o.MaximumPoint.Y - o.MinimumPoint.Y);
                bool fits = o.MinimumPoint.X >= min.X - 1e-6 && o.MinimumPoint.Y >= min.Y - 1e-6 && o.MaximumPoint.X <= max.X + 1e-6 && o.MaximumPoint.Y <= max.Y + 1e-6;
                return $"viewport {Math.Round(w)}x{Math.Round(h)} mm{(fits ? "" : " - extends past the title block, change the scale or crop")}";
            }
            catch { return "viewport"; }
        }
    }
}
