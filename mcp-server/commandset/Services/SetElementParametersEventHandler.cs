using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    /// <summary>One element to change: its id and the instance parameters to set (name -> value as text; lengths in mm, yes/no as true/false).</summary>
    public class ParameterChange
    {
        public long ElementId { get; set; }
        public Dictionary<string, string> Set { get; set; }
    }

    public class ParameterChangeResult
    {
        public long ElementId { get; set; }
        public int Changed { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>Sets instance parameters on existing elements in one transaction. Nothing is placed, moved or deleted.</summary>
    public class SetElementParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<ParameterChange> Changes { get; set; }
        public List<ParameterChangeResult> Results { get; private set; } = new List<ParameterChangeResult>();
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
            Results = new List<ParameterChangeResult>();
            Error = null;
            try
            {
                var doc = app.ActiveUIDocument.Document;
                using (var t = new Transaction(doc, "Set element parameters"))
                {
                    t.Start();
                    ElectricalUtils.MakeSilent(t);
                    foreach (var c in Changes)
                    {
                        var r = new ParameterChangeResult { ElementId = c.ElementId };
                        Results.Add(r);
                        var el = doc.GetElement(ElectricalUtils.ToElementId(c.ElementId));
                        if (el == null) { r.Errors.Add("element not found"); continue; }
                        foreach (var kv in c.Set ?? new Dictionary<string, string>())
                        {
                            if (ElectricalUtils.TrySetParameter(el, kv.Key, kv.Value, out string err)) r.Changed++;
                            else r.Errors.Add(err);
                        }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                // No dialog: a dialog would block the MCP queue.
                Error = ex.Message;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Set element parameters";
    }
}
