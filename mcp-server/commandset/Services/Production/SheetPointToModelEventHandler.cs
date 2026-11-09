using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Production;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Production
{
    /// <summary>
    /// Turns points on a sheet (paper mm, e.g. PDF markup positions) into model coordinates through the viewport under them,
    /// and lists the room and the model elements around each point. Read-only.
    /// </summary>
    public class SheetPointToModelEventHandler : ElectricalHandlerBase<SheetPointRequest, SheetPointResult>
    {
        public override string GetName() => "sheet_point_to_model";

        protected override AIResult<SheetPointResult> Run()
        {
            var req = Input ?? new SheetPointRequest();
            ViewSheet sheet = ProductionUtils.FindSheet(Doc, req.SheetId, req.SheetNumber, out string err);
            if (sheet == null) return Fail(err);
            if (req.Points == null || req.Points.Count == 0) return Fail("No points given.");

            var result = new SheetPointResult { Sheet = $"{sheet.SheetNumber} - {sheet.Name}" };
            var tb = ProductionUtils.TitleBlockOutline(Doc, sheet);
            var viewports = sheet.GetAllViewports().Select(id => Doc.GetElement(id) as Viewport).Where(v => v != null).ToList();

            foreach (var p in req.Points)
            {
                var hit = new SheetPointHit { Id = p.Id };
                result.Points.Add(hit);
                try
                {
                    double xFt, yFt;
                    if (req.PageWidthMm > 0 && req.PageHeightMm > 0 && tb != null)
                    {
                        var s = CadWallDetector.PageToSheet(p.XMm, p.YMm, req.PageWidthMm, req.PageHeightMm,
                            tb.MinimumPoint.X, tb.MinimumPoint.Y, tb.MaximumPoint.X, tb.MaximumPoint.Y);
                        // Fraction of the page -> same fraction of the title block (feet).
                        xFt = s.x;
                        yFt = s.y;
                    }
                    else
                    {
                        double ox = tb?.MinimumPoint.X ?? 0, oy = tb?.MinimumPoint.Y ?? 0;
                        xFt = ox + ElectricalUtils.MmToFt(p.XMm);
                        yFt = oy + ElectricalUtils.MmToFt(p.YMm);
                    }
                    var onSheet = new XYZ(xFt, yFt, 0);
                    hit.SheetXMm = Math.Round(ElectricalUtils.FtToMm(xFt), 1);
                    hit.SheetYMm = Math.Round(ElectricalUtils.FtToMm(yFt), 1);

                    // Smallest viewport containing the point wins (a detail viewport may sit over a plan).
                    Viewport vp = viewports
                        .Select(v => new { v, o = v.GetBoxOutline() })
                        .Where(x => x.o.Contains(onSheet, 1e-6) || (onSheet.X >= x.o.MinimumPoint.X && onSheet.X <= x.o.MaximumPoint.X
                                                                  && onSheet.Y >= x.o.MinimumPoint.Y && onSheet.Y <= x.o.MaximumPoint.Y))
                        .OrderBy(x => (x.o.MaximumPoint.X - x.o.MinimumPoint.X) * (x.o.MaximumPoint.Y - x.o.MinimumPoint.Y))
                        .Select(x => x.v).FirstOrDefault();
                    if (vp == null) { hit.Error = "No viewport under this point (title block, legend area or empty paper)."; continue; }

                    var view = Doc.GetElement(vp.ViewId) as View;
                    hit.ViewId = view.Id.GetValue();
                    hit.ViewName = view.Name;
                    if (view.GenLevel != null) hit.Level = view.GenLevel.Name;

                    XYZ model = ToModel(vp, view, onSheet);
                    if (model == null) { hit.Error = $"View '{view.Name}' ({view.ViewType}) cannot map sheet points to the model."; continue; }
                    if (view.GenLevel != null) model = new XYZ(model.X, model.Y, view.GenLevel.Elevation);
                    hit.Model = new[] { Mm(model.X), Mm(model.Y), Mm(model.Z) };

                    var probe = new XYZ(model.X, model.Y, model.Z + ElectricalUtils.MmToFt(1000));
                    Room room = Doc.GetRoomAtPoint(probe);
                    if (room != null) hit.Room = $"{room.Number} {room.Name}".Trim();

                    if (req.NearbyRadiusMm > 0) hit.Nearby = Nearby(view, model, ElectricalUtils.MmToFt(req.NearbyRadiusMm));
                }
                catch (Exception ex) { hit.Error = ex.Message; }
            }
            int ok = result.Points.Count(h => h.Model != null);
            return Ok(result, $"{ok} of {result.Points.Count} point(s) mapped to the model on sheet {sheet.SheetNumber}.");
        }

        private static double Mm(double ft) => Math.Round(ElectricalUtils.FtToMm(ft), 1);

        private static XYZ ToModel(Viewport vp, View view, XYZ onSheet)
        {
#if REVIT2022_OR_GREATER
            Transform projToSheet = vp.GetProjectionToSheetTransform();
            var transforms = view.GetModelToProjectionTransforms();
            if (transforms == null || transforms.Count == 0) return null;
            Transform modelToProj = transforms[0].GetModelToProjectionTransform();
            XYZ proj = projToSheet.Inverse.OfPoint(onSheet);
            return modelToProj.Inverse.OfPoint(new XYZ(proj.X, proj.Y, 0));
#else
            // Older Revit: viewport centre <-> view crop centre, divided by scale (plans only).
            if (!(view is ViewPlan) || !view.CropBoxActive) return null;
            XYZ c = vp.GetBoxCenter();
            var cb = view.CropBox;
            XYZ cropCentre = cb.Transform.OfPoint((cb.Min + cb.Max) / 2);
            return new XYZ(cropCentre.X + (onSheet.X - c.X) * view.Scale, cropCentre.Y + (onSheet.Y - c.Y) * view.Scale, 0);
#endif
        }

        private List<NearbyElement> Nearby(View view, XYZ at, double radiusFt)
        {
            var outline = new Outline(new XYZ(at.X - radiusFt, at.Y - radiusFt, at.Z - 1000), new XYZ(at.X + radiusFt, at.Y + radiusFt, at.Z + 1000));
            var list = new FilteredElementCollector(Doc, view.Id)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model && !(e is Room))
                .Select(e =>
                {
                    var bb = e.get_BoundingBox(view);
                    XYZ c = bb == null ? at : (bb.Min + bb.Max) / 2;
                    double d = Math.Sqrt((c.X - at.X) * (c.X - at.X) + (c.Y - at.Y) * (c.Y - at.Y));
                    string name = e is FamilyInstance fi ? $"{fi.Symbol.FamilyName}: {fi.Symbol.Name}" : e.Name;
                    return new NearbyElement { Id = e.Id.GetValue(), Category = e.Category.Name, Name = name, DistanceMm = Mm(d) };
                })
                .OrderBy(n => n.DistanceMm).Take(25).ToList();
            return list;
        }
    }
}
