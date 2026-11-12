using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Documentation
{
    public class NestedSymbolInfo
    {
        public string Family { get; set; }
        public string Type { get; set; }
        public string Category { get; set; }
        public bool Visible { get; set; }
        public string VisibleBy { get; set; }
        public double XMm { get; set; }
        public double YMm { get; set; }
        public double RotationDeg { get; set; }
        /// <summary>Nested parameter -> value the host type gives it (through an associated family parameter).</summary>
        public Dictionary<string, string> Driven { get; set; } = new Dictionary<string, string>();
    }

    public class LegendSwapResult
    {
        public long ElementId { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public List<NestedSymbolInfo> Nested { get; set; } = new List<NestedSymbolInfo>();
        public List<long> PlacedIds { get; set; } = new List<long>();
        public bool OriginalDeleted { get; set; }
        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>
    /// A legend component cannot be rotated. This reads the 2D symbols nested in the component's family and, when Apply is set,
    /// loads them into the project, places them in the legend at the component's position, rotated, and deletes the component.
    /// </summary>
    public class SwapLegendSymbolsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<long> ElementIds { get; set; }
        public double ClockwiseDegrees { get; set; }
        public bool Apply { get; set; }
        public bool DeleteOriginals { get; set; }
        public List<LegendSwapResult> Results { get; private set; } = new List<LegendSwapResult>();
        public string Error { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        private class Planned
        {
            public LegendSwapResult Result;
            public ElementId OriginalId;
            public ElementId FamilyId;
            public string TypeName;
            public XYZ Point;
            public double Angle;
            public List<KeyValuePair<string, object>> Values = new List<KeyValuePair<string, object>>();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            Results = new List<LegendSwapResult>();
            Error = null;
            var famDocs = new Dictionary<long, Document>();
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var view = doc.ActiveView;
                double turn = -ClockwiseDegrees * Math.PI / 180.0;
                var plans = new List<Planned>();

                // Phase 1 (no transaction): read the nested symbols, load their families into the project.
                foreach (var id in ElementIds)
                {
                    var r = new LegendSwapResult { ElementId = id };
                    Results.Add(r);
                    var el = doc.GetElement(ElectricalUtils.ToElementId(id));
                    var typeId = el?.get_Parameter(BuiltInParameter.LEGEND_COMPONENT)?.AsElementId();
                    var sym = typeId == null ? null : doc.GetElement(typeId) as FamilySymbol;
                    if (sym == null) { r.Notes.Add("not a legend component of a loadable family"); continue; }
                    r.Family = sym.FamilyName;
                    r.Type = sym.Name;
                    var bb = el.get_BoundingBox(view);
                    if (bb == null) { r.Notes.Add("no extent in the active view"); continue; }
                    var centre = (bb.Min + bb.Max) / 2.0;

                    long famKey = sym.Family.Id.GetValue();
                    if (!famDocs.TryGetValue(famKey, out var famDoc))
                    {
                        try { famDoc = doc.EditFamily(sym.Family); }
                        catch (Exception ex) { r.Notes.Add("family cannot be opened: " + ex.Message); continue; }
                        famDocs[famKey] = famDoc;
                    }
                    var fm = famDoc.FamilyManager;
                    var ft = fm.Types.Cast<FamilyType>().FirstOrDefault(t => t.Name == sym.Name);
                    if (ft == null) r.Notes.Add("type not found inside the family; visibility read from the family's current type");

                    var nested = new FilteredElementCollector(famDoc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                        .Where(fi => fi.Category != null &&
                            (fi.Category.Id.GetValue() == (long)BuiltInCategory.OST_GenericAnnotation ||
                             fi.Category.Id.GetValue() == (long)BuiltInCategory.OST_DetailComponents))
                        .ToList();
                    if (nested.Count == 0) r.Notes.Add("the family has no nested 2D symbol (its plan symbol is drawn with lines)");

                    var visible = new List<(FamilyInstance fi, NestedSymbolInfo info, List<KeyValuePair<string, object>> values)>();
                    foreach (var fi in nested)
                    {
                        var info = new NestedSymbolInfo { Family = fi.Symbol.FamilyName, Type = fi.Symbol.Name, Category = fi.Category.Name, Visible = true };
                        if (fi.Location is LocationPoint lp)
                        {
                            info.XMm = Math.Round(ElectricalUtils.FtToMm(lp.Point.X), 1);
                            info.YMm = Math.Round(ElectricalUtils.FtToMm(lp.Point.Y), 1);
                            info.RotationDeg = Math.Round(lp.Rotation * 180.0 / Math.PI, 1);
                        }
                        var values = new List<KeyValuePair<string, object>>();
                        var vis = fi.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);
                        foreach (Parameter q in fi.Parameters.Cast<Parameter>().Concat(fi.Symbol.Parameters.Cast<Parameter>()))
                        {
                            FamilyParameter fp;
                            try { fp = fm.GetAssociatedFamilyParameter(q); } catch { fp = null; }
                            if (fp == null) continue;
                            object v = ValueOf(ft ?? fm.CurrentType, fp);
                            if (vis != null && q.Id == vis.Id)
                            {
                                info.VisibleBy = fp.Definition.Name;
                                info.Visible = v is int i ? i != 0 : true;
                                continue;
                            }
                            if (v == null) continue;
                            info.Driven[q.Definition.Name] = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                            values.Add(new KeyValuePair<string, object>(q.Definition.Name, v));
                        }
                        if (info.VisibleBy == null && vis != null) info.Visible = vis.AsInteger() != 0;
                        r.Nested.Add(info);
                        if (info.Visible) visible.Add((fi, info, values));
                    }
                    if (!Apply || visible.Count == 0) continue;

                    // Keep the symbols' layout relative to each other, centred on the legend component.
                    var pts = visible.Select(v => (v.fi.Location as LocationPoint)?.Point ?? XYZ.Zero).ToList();
                    var mean = new XYZ(pts.Average(p => p.X), pts.Average(p => p.Y), 0);
                    for (int k = 0; k < visible.Count; k++)
                    {
                        var (fi, info, values) = visible[k];
                        var projFamily = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                            .FirstOrDefault(f => f.Name == fi.Symbol.FamilyName && f.FamilyCategory?.Id.GetValue() == fi.Category.Id.GetValue());
                        if (projFamily == null)
                        {
                            Document nd = null;
                            try
                            {
                                nd = famDoc.EditFamily(fi.Symbol.Family);
                                projFamily = nd.LoadFamily(doc, new FamilyBuildUtils.OverwriteFamilyLoadOptions());
                            }
                            catch (Exception ex) { r.Notes.Add($"'{info.Family}' could not be loaded into the project: {ex.Message}"); }
                            finally { try { nd?.Close(false); } catch { } }
                        }
                        if (projFamily == null) continue;
                        var d = new XYZ(pts[k].X - mean.X, pts[k].Y - mean.Y, 0);
                        var rot = new XYZ(d.X * Math.Cos(turn) - d.Y * Math.Sin(turn), d.X * Math.Sin(turn) + d.Y * Math.Cos(turn), 0);
                        plans.Add(new Planned
                        {
                            Result = r, OriginalId = el.Id, FamilyId = projFamily.Id, TypeName = fi.Symbol.Name,
                            Point = centre + rot, Angle = turn + ((fi.Location as LocationPoint)?.Rotation ?? 0), Values = values,
                        });
                    }
                }

                foreach (var fd in famDocs.Values) { try { fd.Close(false); } catch { } }
                famDocs.Clear();

                // Phase 2: place, rotate, delete the originals.
                if (Apply && plans.Count > 0)
                {
                    using (var t = new Transaction(doc, "Swap legend components for rotated symbols"))
                    {
                        t.Start();
                        ElectricalUtils.MakeSilent(t);
                        foreach (var p in plans)
                        {
                            try
                            {
                                var family = doc.GetElement(p.FamilyId) as Family;
                                var symbols = family.GetFamilySymbolIds().Select(i => doc.GetElement(i) as FamilySymbol).ToList();
                                var symbol = symbols.FirstOrDefault(s => s.Name == p.TypeName) ?? symbols.First();
                                if (!symbol.IsActive) symbol.Activate();
                                var inst = doc.Create.NewFamilyInstance(p.Point, symbol, view);
                                foreach (var kv in p.Values)
                                {
                                    var q = inst.LookupParameter(kv.Key);
                                    if (q == null || q.IsReadOnly) { p.Result.Notes.Add($"'{kv.Key}' = {kv.Value} is a type value of '{symbol.FamilyName}', not set"); continue; }
                                    if (kv.Value is string s) q.Set(s);
                                    else if (kv.Value is int n) q.Set(n);
                                    else if (kv.Value is double x) q.Set(x);
                                }
                                if (Math.Abs(p.Angle) > 1e-9)
                                    ElementTransformUtils.RotateElement(doc, inst.Id, Line.CreateBound(p.Point, p.Point + XYZ.BasisZ), p.Angle);
                                p.Result.PlacedIds.Add(inst.Id.GetValue());
                            }
                            catch (Exception ex) { p.Result.Notes.Add("placing failed: " + ex.Message); }
                        }
                        if (DeleteOriginals)
                        {
                            foreach (var g in plans.GroupBy(p => p.Result))
                            {
                                if (g.Key.PlacedIds.Count != g.Count()) { g.Key.Notes.Add("original kept: not every symbol was placed"); continue; }
                                try { doc.Delete(g.First().OriginalId); g.Key.OriginalDeleted = true; }
                                catch (Exception ex) { g.Key.Notes.Add("original not deleted: " + ex.Message); }
                            }
                        }
                        t.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                foreach (var fd in famDocs.Values) { try { fd.Close(false); } catch { } }
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private static object ValueOf(FamilyType ft, FamilyParameter fp)
        {
            if (ft == null || !ft.HasValue(fp)) return null;
            switch (fp.StorageType)
            {
                case StorageType.String: return ft.AsString(fp);
                case StorageType.Integer: return ft.AsInteger(fp);
                case StorageType.Double: return ft.AsDouble(fp);
                default: return null; // element ids differ between the family and the project
            }
        }

        public string GetName() => "Swap legend symbols";
    }
}
