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
        /// <summary>Optional: a loaded host family that nests this symbol. The symbol is then taken from that host (see NestedSymbolLoader).</summary>
        public string HostFamilyName { get; set; }
        /// <summary>With HostFamilyName: load the host's copy as "name (host)" even when it looks the same as the loaded family.</summary>
        public bool Separate { get; set; }
        /// <summary>Instance parameters to set on the placed symbol (name -> value as text).</summary>
        public Dictionary<string, string> Set { get; set; }
        public double XMm { get; set; }
        public double YMm { get; set; }
        public double ClockwiseDegrees { get; set; }
    }

    public class ViewSymbolResult
    {
        public string FamilyName { get; set; }
        public string PlacedFamily { get; set; }
        public long ElementId { get; set; }
        public List<string> Notes { get; set; } = new List<string>();
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

                bool Is2D(Family f) => f.FamilyCategory != null &&
                    (f.FamilyCategory.Id.GetValue() == (long)BuiltInCategory.OST_GenericAnnotation ||
                     f.FamilyCategory.Id.GetValue() == (long)BuiltInCategory.OST_DetailComponents);

                // Before the transaction: symbols that must come out of a host family.
                var fromHost = new Dictionary<ViewSymbolRequest, ElementId>();
                var hostNotes = new Dictionary<ViewSymbolRequest, List<string>>();
                var hostErrors = new Dictionary<ViewSymbolRequest, string>();
                foreach (var s in Symbols.Where(x => !string.IsNullOrWhiteSpace(x.HostFamilyName)))
                {
                    var host = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => f.Name == s.HostFamilyName);
                    if (host == null) { hostErrors[s] = $"host family '{s.HostFamilyName}' is not loaded"; continue; }
                    Document hd = null;
                    try
                    {
                        hd = doc.EditFamily(host);
                        var nested = new FilteredElementCollector(hd).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => f.Name == s.FamilyName && Is2D(f));
                        if (nested == null) { hostErrors[s] = $"'{s.HostFamilyName}' nests no 2D symbol family named '{s.FamilyName}'"; continue; }
                        var notes = new List<string>();
                        var fam = NestedSymbolLoader.Ensure(doc, hd, nested, host.Name, notes, s.Separate);
                        if (fam == null) hostErrors[s] = string.Join("; ", notes); else { fromHost[s] = fam.Id; hostNotes[s] = notes; }
                    }
                    catch (Exception ex) { hostErrors[s] = ex.Message; }
                    finally { try { hd?.Close(false); } catch { } }
                }

                var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().Where(Is2D).ToList();

                using (var t = new Transaction(doc, "Place view symbols"))
                {
                    t.Start();
                    ElectricalUtils.MakeSilent(t);
                    foreach (var s in Symbols)
                    {
                        var r = new ViewSymbolResult { FamilyName = s.FamilyName };
                        Results.Add(r);
                        if (hostErrors.TryGetValue(s, out var he)) { r.Error = he; continue; }
                        var family = fromHost.TryGetValue(s, out var fid) ? doc.GetElement(fid) as Family : families.FirstOrDefault(f => f.Name == s.FamilyName);
                        if (family == null) { r.Error = "no loaded generic annotation or detail item family with this exact name"; continue; }
                        r.PlacedFamily = family.Name;
                        if (hostNotes.TryGetValue(s, out var hn)) r.Notes.AddRange(hn);
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
                                foreach (var kv in s.Set ?? new Dictionary<string, string>())
                                    if (!ElectricalUtils.TrySetParameter(inst, kv.Key, kv.Value, out string perr)) r.Notes.Add(perr);
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
