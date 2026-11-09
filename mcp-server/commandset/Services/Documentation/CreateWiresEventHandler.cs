using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>
    /// Draws wiring for existing circuits in a plan view: device-to-device wires in nearest-neighbour order
    /// starting from the device closest to the panel, plus a home run towards the panel.
    /// </summary>
    public class CreateWiresEventHandler : ElectricalHandlerBase<WireRequest, WireResult>
    {
        public override string GetName() => "create_wires";

        protected override AIResult<WireResult> Run()
        {
            var req = Input ?? new WireRequest();
            View view = DocumentationUtils.ResolveView(Doc, UiApp.ActiveUIDocument.ActiveView, req.ViewId, out string viewError);
            if (view == null) return Fail(viewError);
            if (!(view is ViewPlan plan) || plan.GenLevel == null) return Fail($"'{view.Name}' is not a plan view. Wires are drawn in floor/ceiling plans.");
            var result = new WireResult { ViewId = view.Id.GetValue() };

            var wireTypes = new FilteredElementCollector(Doc).OfClass(typeof(WireType)).Cast<WireType>().ToList();
            var wireType = DocumentationUtils.PickByName(wireTypes, w => w.Name, req.WireTypeName) ?? wireTypes.FirstOrDefault();
            if (wireType == null) return Fail("The project has no wire type (use an electrical template).");
            var wiringType = (req.WiringType ?? "arc").ToLowerInvariant().StartsWith("ch") ? WiringType.Chamfer : WiringType.Arc;

            // Circuits
            var all = new FilteredElementCollector(Doc).OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>().ToList();
            var circuits = new List<ElectricalSystem>();
            foreach (long id in req.CircuitIds ?? new List<long>())
            {
                var c = all.FirstOrDefault(s => s.Id.GetValue() == id);
                if (c != null) circuits.Add(c); else result.Errors.Add($"{id} is not a circuit.");
            }
            foreach (var pn in req.PanelNames ?? new List<string>())
            {
                var matches = all.Where(s => DocumentationUtils.SameText(s.PanelName, pn)).ToList();
                if (matches.Count == 0) result.Errors.Add($"No circuits on panel '{pn}'.");
                circuits.AddRange(matches);
            }
            circuits = circuits.GroupBy(c => c.Id.GetValue()).Select(g => g.First()).ToList();
            if (circuits.Count == 0) return Fail("No circuits to wire. Give circuitIds or panelNames. " + string.Join(" ", result.Errors));

            var circuitsWithWires = new HashSet<long>();
            if (req.SkipCircuitsWithWires)
            {
                foreach (var w in new FilteredElementCollector(Doc, view.Id).OfClass(typeof(Wire)).Cast<Wire>())
                {
                    try { if (w.MEPSystem != null) circuitsWithWires.Add(w.MEPSystem.Id.GetValue()); } catch { }
                }
            }

            double levelZ = plan.GenLevel.Elevation;
            double homeRun = DocumentationUtils.PaperToModelFt(view, req.HomeRunLength);
            var visible = new HashSet<long>(new FilteredElementCollector(Doc, view.Id).WhereElementIsNotElementType().ToElementIds().Select(i => i.GetValue()));

            using (var t = new Transaction(Doc, "MCP: create wires"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);
                foreach (var circuit in circuits)
                {
                    string label = $"{circuit.PanelName}/{circuit.CircuitNumber}";
                    if (circuitsWithWires.Contains(circuit.Id.GetValue())) { result.Skipped.Add($"{label}: already has wires in this view"); continue; }

                    var devices = new List<(Element el, Connector con)>();
                    foreach (Element e in circuit.Elements)
                    {
                        if (!visible.Contains(e.Id.GetValue())) continue;
                        var con = CircuitConnector(e, circuit);
                        if (con != null) devices.Add((e, con));
                    }
                    if (devices.Count == 0) { result.Skipped.Add($"{label}: no device of this circuit is visible in the view"); continue; }

                    XYZ panelPoint = (circuit.BaseEquipment?.Location as LocationPoint)?.Point;
                    var ordered = Order(devices, panelPoint);
                    int made = 0;
                    for (int i = 0; i + 1 < ordered.Count; i++)
                    {
                        if (TryWire(view, wireType, wiringType, ordered[i].con, ordered[i + 1].con, null, levelZ, result, label)) made++;
                    }
                    if (req.HomeRun)
                    {
                        XYZ start = Flat(ordered[0].con.Origin, levelZ);
                        XYZ dir = panelPoint != null ? Flat(panelPoint, levelZ) - start : XYZ.BasisX;
                        dir = dir.GetLength() < 1e-6 ? XYZ.BasisX : dir.Normalize();
                        XYZ end = start + dir * homeRun;
                        if (TryWire(view, wireType, wiringType, ordered[0].con, null, end, levelZ, result, label + " home run")) made++;
                    }
                    if (made > 0) { result.CircuitsWired++; result.Wires += made; }
                }
                t.Commit();
                Warnings.AddRange(pre.Messages.Take(10));
                if (pre.RolledBack) return Fail("Revit rolled the wires back.");
            }

            string msg = $"Drew {result.Wires} wire(s) for {result.CircuitsWired} circuit(s) in '{view.Name}'.";
            if (result.Wires == 0) return Fail(msg + " " + string.Join(" ", result.Errors.Concat(result.Skipped).Take(10)));
            return Ok(result, msg);
        }

        private static XYZ Flat(XYZ p, double z) => new XYZ(p.X, p.Y, z);

        private bool TryWire(View view, WireType type, WiringType wiring, Connector from, Connector to, XYZ freeEnd, double z, WireResult result, string label)
        {
            try
            {
                XYZ a = Flat(from.Origin, z);
                XYZ b = to != null ? Flat(to.Origin, z) : freeEnd;
                if (a.DistanceTo(b) < 1e-3) return false;
                XYZ dir = (b - a).Normalize();
                XYZ normal = new XYZ(-dir.Y, dir.X, 0);
                XYZ mid = (a + b) / 2 + normal * (a.DistanceTo(b) * 0.15);
                var pts = new List<XYZ> { a, mid, b };
                Wire.Create(Doc, type.Id, view.Id, wiring, pts, from, to);
                return true;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{label}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Start at the device nearest to the panel, then always go to the nearest unvisited device.</summary>
        private static List<(Element el, Connector con)> Order(List<(Element el, Connector con)> devices, XYZ panel)
        {
            var left = devices.ToList();
            var ordered = new List<(Element el, Connector con)>();
            XYZ current = panel ?? left[0].con.Origin;
            while (left.Count > 0)
            {
                var next = left.OrderBy(d => Dist2D(d.con.Origin, current)).First();
                ordered.Add(next);
                left.Remove(next);
                current = next.con.Origin;
            }
            return ordered;
        }

        private static double Dist2D(XYZ a, XYZ b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static Connector CircuitConnector(Element e, ElectricalSystem circuit)
        {
            var mgr = (e as FamilyInstance)?.MEPModel?.ConnectorManager;
            if (mgr == null) return null;
            Connector fallback = null;
            foreach (Connector c in mgr.Connectors)
            {
                if (c.Domain != Domain.DomainElectrical) continue;
                fallback = fallback ?? c;
                try { if (c.MEPSystem != null && c.MEPSystem.Id == circuit.Id) return c; } catch { }
            }
            return fallback;
        }
    }
}
