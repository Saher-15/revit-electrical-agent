using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>One 2D symbol (generic annotation or detail item family already loaded in the project) to place in a view.</summary>
    public class ViewSymbolRequest
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public double XMm { get; set; }
        public double YMm { get; set; }
        public double ClockwiseDegrees { get; set; }
    }

    public class ViewSymbolResult
    {
        public string FamilyName { get; set; }
        public long ElementId { get; set; }
        public string Error { get; set; }
    }

    /// <summary>Places loaded 2D symbols in a legend, drafting or plan view named by id (the active view only when no id is given).</summary>
    public class PlaceViewSymbolsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public long ViewId { get; set; }
        public List<ViewSymbolRequest> Symbols { get; set; }
        public string ViewName { get; private set; }
        public List<ViewSymbolResult> Results { get; private set; } = new List<ViewSymbolResult>();
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
            Results = new List<ViewSymbolResult>();
            Error = null;
            ViewName = null;
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var view = ViewId > 0 ? doc.GetElement(ElectricalUtils.ToElementId(ViewId)) as View : doc.ActiveView;
                if (view == null) { Error = $"view {ViewId} not found"; return; }
                ViewName = view.Name;

                var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                    .Where(f => f.FamilyCategory != null &&
                        (f.FamilyCategory.Id.GetValue() == (long)BuiltInCategory.OST_GenericAnnotation ||
                         f.FamilyCategory.Id.GetValue() == (long)BuiltInCategory.OST_DetailComponents))
                    .ToList();

                using (var t = new Transaction(doc, "Place view symbols"))
                {
                    t.Start();
                    ElectricalUtils.MakeSilent(t);
                    foreach (var s in Symbols)
                    {
                        var r = new ViewSymbolResult { FamilyName = s.FamilyName };
                        Results.Add(r);
                        var family = families.FirstOrDefault(f => f.Name == s.FamilyName);
                        if (family == null) { r.Error = "no loaded generic annotation or detail item family with this exact name"; continue; }
                        using (var st = new SubTransaction(doc))
                        {
                            try
                            {
                                st.Start();
                                var types = family.GetFamilySymbolIds().Select(i => doc.GetElement(i) as FamilySymbol).ToList();
                                var symbol = types.FirstOrDefault(x => x.Name == s.TypeName) ?? types.First();
                                if (!symbol.IsActive) symbol.Activate();
                                var point = new XYZ(ElectricalUtils.MmToFt(s.XMm), ElectricalUtils.MmToFt(s.YMm), 0);
                                var inst = doc.Create.NewFamilyInstance(point, symbol, view);
                                if (Math.Abs(s.ClockwiseDegrees) > 1e-9)
                                    ElementTransformUtils.RotateElement(doc, inst.Id, Line.CreateBound(point, point + XYZ.BasisZ), -s.ClockwiseDegrees * Math.PI / 180.0);
                                st.Commit();
                                r.ElementId = inst.Id.GetValue();
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

        public string GetName() => "Place view symbols";
    }
}
