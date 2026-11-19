using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class RotateResult
    {
        public long ElementId { get; set; }
        public bool Rotated { get; set; }
        public string Error { get; set; }
    }

    /// <summary>Rotates existing elements about the vertical axis through their own bounding-box centre (or one common centre). Each element has its own sub-transaction, so one that Revit refuses (e.g. a legend component) does not undo the rest.</summary>
    public class RotateElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<long> ElementIds { get; set; }
        /// <summary>Clockwise degrees, as seen in the view (Revit's own angle is counter-clockwise).</summary>
        public double ClockwiseDegrees { get; set; }
        public bool CommonCenter { get; set; }
        public List<RotateResult> Results { get; private set; } = new List<RotateResult>();
        public string Error { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            Results = new List<RotateResult>();
            Error = null;
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var view = doc.ActiveView;
                double angle = -ClockwiseDegrees * Math.PI / 180.0;

                var centres = new Dictionary<long, XYZ>();
                foreach (var id in ElementIds)
                {
                    var el = doc.GetElement(ElectricalUtils.ToElementId(id));
                    var bb = el?.get_BoundingBox(view) ?? el?.get_BoundingBox(null);
                    if (bb != null) centres[id] = (bb.Min + bb.Max) / 2.0;
                }
                XYZ common = null;
                if (CommonCenter && centres.Count > 0)
                    common = new XYZ(centres.Values.Average(p => p.X), centres.Values.Average(p => p.Y), centres.Values.Average(p => p.Z));

                using (var t = new Transaction(doc, "Rotate elements"))
                {
                    t.Start();
                    ElectricalUtils.MakeSilent(t);
                    foreach (var id in ElementIds)
                    {
                        var r = new RotateResult { ElementId = id };
                        Results.Add(r);
                        if (!centres.TryGetValue(id, out var c)) { r.Error = "element not found or has no extent in this view"; continue; }
                        var centre = common ?? c;
                        using (var st = new SubTransaction(doc))
                        {
                            try
                            {
                                st.Start();
                                var axis = Line.CreateBound(centre, centre + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, ElectricalUtils.ToElementId(id), axis, angle);
                                st.Commit();
                                r.Rotated = true;
                            }
                            catch (Exception ex)
                            {
                                st.RollBack();
                                r.Error = ex.Message;
                            }
                        }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Rotate elements";
    }
}
