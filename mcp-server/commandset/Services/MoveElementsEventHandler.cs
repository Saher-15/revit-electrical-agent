using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    /// <summary>One element and how far to move it, in mm along the model axes (in a plan, legend or drafting view: X = right, Y = up).</summary>
    public class ElementMove
    {
        public long ElementId { get; set; }
        public double DxMm { get; set; }
        public double DyMm { get; set; }
        public double DzMm { get; set; }
    }

    public class MoveResult
    {
        public long ElementId { get; set; }
        public bool Moved { get; set; }
        public string Error { get; set; }
    }

    /// <summary>Moves existing elements. Each element has its own sub-transaction, so one that Revit refuses does not undo the rest.</summary>
    public class MoveElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<ElementMove> Moves { get; set; }
        public List<MoveResult> Results { get; private set; } = new List<MoveResult>();
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
            Results = new List<MoveResult>();
            Error = null;
            try
            {
                var doc = app.ActiveUIDocument.Document;
                using (var t = new Transaction(doc, "Move elements"))
                {
                    t.Start();
                    ElectricalUtils.MakeSilent(t);
                    foreach (var m in Moves)
                    {
                        var r = new MoveResult { ElementId = m.ElementId };
                        Results.Add(r);
                        var id = ElectricalUtils.ToElementId(m.ElementId);
                        if (doc.GetElement(id) == null) { r.Error = "element not found"; continue; }
                        using (var st = new SubTransaction(doc))
                        {
                            try
                            {
                                st.Start();
                                ElementTransformUtils.MoveElement(doc, id,
                                    new XYZ(ElectricalUtils.MmToFt(m.DxMm), ElectricalUtils.MmToFt(m.DyMm), ElectricalUtils.MmToFt(m.DzMm)));
                                st.Commit();
                                r.Moved = true;
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

        public string GetName() => "Move elements";
    }
}
