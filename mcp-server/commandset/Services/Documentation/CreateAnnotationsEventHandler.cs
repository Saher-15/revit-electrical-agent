using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>Text notes, detail lines/polylines and circles in a view (plans, drafting views, sheets).</summary>
    public class CreateAnnotationsEventHandler : ElectricalHandlerBase<AnnotationRequest, AnnotationResult>
    {
        public override string GetName() => "create_annotations";

        protected override AIResult<AnnotationResult> Run()
        {
            var req = Input ?? new AnnotationRequest();
            View view = DocumentationUtils.ResolveView(Doc, UiApp.ActiveUIDocument.ActiveView, req.ViewId, out string viewError);
            if (view == null) return Fail(viewError);
            if (view is ViewSchedule || view.ViewType == ViewType.ThreeD) return Fail($"'{view.Name}' ({view.ViewType}) cannot hold text or detail lines.");
            var result = new AnnotationResult { ViewId = view.Id.GetValue() };

            var textTypes = new FilteredElementCollector(Doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
            ElementId defaultText = Doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (defaultText == ElementId.InvalidElementId && textTypes.Count > 0) defaultText = textTypes[0].Id;

            using (var t = new Transaction(Doc, "MCP: annotations"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);

                foreach (var tx in req.Texts ?? new List<TextNoteInfo>())
                {
                    try
                    {
                        if (string.IsNullOrEmpty(tx.Text)) continue;
                        var type = DocumentationUtils.PickByName(textTypes, x => x.Name, tx.TextTypeName);
                        if (!string.IsNullOrWhiteSpace(tx.TextTypeName) && type == null) result.Errors.Add($"Text type '{tx.TextTypeName}' not found; default used.");
                        var opts = new TextNoteOptions(type?.Id ?? defaultText)
                        {
                            HorizontalAlignment = HorizontalTextAlignment.Left,
                            Rotation = tx.Rotation * Math.PI / 180.0,
                        };
                        XYZ p = Point(view, tx.X, tx.Y);
                        TextNote note = tx.Width > 0
                            ? TextNote.Create(Doc, view.Id, p, ElectricalUtils.MmToFt(tx.Width), tx.Text, opts)
                            : TextNote.Create(Doc, view.Id, p, tx.Text, opts);
                        result.TextIds.Add(note.Id.GetValue());
                    }
                    catch (Exception ex) { result.Errors.Add($"Text '{tx.Text}': {ex.Message}"); }
                }

                foreach (var ln in req.Lines ?? new List<DetailLineInfo>())
                {
                    var pts = (ln.Points ?? new List<JZPoint>()).Select(p => Point(view, p.X, p.Y)).ToList();
                    if (ln.Closed && pts.Count > 2) pts.Add(pts[0]);
                    var style = DocumentationUtils.FindLineStyle(Doc, ln.LineStyle);
                    if (!string.IsNullOrWhiteSpace(ln.LineStyle) && style == null) result.Errors.Add($"Line style '{ln.LineStyle}' not found; default used.");
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        try
                        {
                            if (pts[i].DistanceTo(pts[i + 1]) < Doc.Application.ShortCurveTolerance) continue;
                            var curve = Doc.Create.NewDetailCurve(view, Line.CreateBound(pts[i], pts[i + 1]));
                            if (style != null) curve.LineStyle = style;
                            result.LineIds.Add(curve.Id.GetValue());
                        }
                        catch (Exception ex) { result.Errors.Add($"Line segment {i}: {ex.Message}"); }
                    }
                }

                foreach (var c in req.Circles ?? new List<DetailCircleInfo>())
                {
                    try
                    {
                        XYZ center = Point(view, c.X, c.Y);
                        var arc = Arc.Create(center, ElectricalUtils.MmToFt(c.Radius), 0, 2 * Math.PI, view.RightDirection, view.UpDirection);
                        var curve = Doc.Create.NewDetailCurve(view, arc);
                        var style = DocumentationUtils.FindLineStyle(Doc, c.LineStyle);
                        if (style != null) curve.LineStyle = style;
                        result.LineIds.Add(curve.Id.GetValue());
                    }
                    catch (Exception ex) { result.Errors.Add($"Circle: {ex.Message}"); }
                }

                t.Commit();
                Warnings.AddRange(pre.Messages.Take(10));
                if (pre.RolledBack) return Fail("Revit rolled the annotations back.");
            }

            string msg = $"{result.TextIds.Count} text note(s), {result.LineIds.Count} detail curve(s) in '{view.Name}'.";
            if (result.TextIds.Count + result.LineIds.Count == 0) return Fail(msg + " " + string.Join(" ", result.Errors));
            return Ok(result, msg);
        }

        /// <summary>Sheets use paper mm from the sheet origin; other views use DocumentationUtils.ViewPoint.</summary>
        private static XYZ Point(View view, double x, double y) =>
            view is ViewSheet ? new XYZ(ElectricalUtils.MmToFt(x), ElectricalUtils.MmToFt(y), 0) : DocumentationUtils.ViewPoint(view, x, y);
    }
}
