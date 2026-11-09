using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Electrical
{
    public enum RacewayKind { CableTray, Conduit }

    /// <summary>
    /// Creates polyline runs of cable tray or conduit, with elbow fittings
    /// between consecutive segments when the project has fitting families.
    /// </summary>
    public class CreateRacewayEventHandler : ElectricalHandlerBase<List<RacewayRunInfo>, List<RacewayRunResult>>
    {
        private readonly RacewayKind _kind;

        public CreateRacewayEventHandler(RacewayKind kind)
        {
            _kind = kind;
        }

        public override string GetName() => _kind == RacewayKind.CableTray ? "create_cable_tray" : "create_conduit";

        protected override AIResult<List<RacewayRunResult>> Run()
        {
            if (Input == null || Input.Count == 0)
                return Fail("No runs were given.");

            var results = new List<RacewayRunResult>();
            using (var tg = new TransactionGroup(Doc, $"MCP: {GetName()}"))
            {
                tg.Start();
                foreach (var run in Input)
                {
                    var r = new RacewayRunResult { Name = run.Name };
                    results.Add(r);
                    using (var t = new Transaction(Doc, $"{GetName()} {run.Name}"))
                    {
                        t.Start();
                        ElectricalUtils.MakeSilent(t);
                        try
                        {
                            CreateRun(run, r);
                            if (r.SegmentIds.Count > 0) t.Commit(); else t.RollBack();
                        }
                        catch (Exception ex)
                        {
                            r.Error = ex.Message;
                            r.SegmentIds.Clear();
                            r.FittingIds.Clear();
                            t.RollBack();
                        }
                    }
                }
                tg.Assimilate();
            }

            int ok = results.Count(x => x.SegmentIds.Count > 0);
            string msg = $"Created {ok} of {results.Count} run(s), {results.Sum(x => x.SegmentIds.Count)} segment(s), {results.Sum(x => x.FittingIds.Count)} fitting(s).";
            if (ok == 0) return Fail(msg + " " + string.Join(" | ", results.Where(x => x.Error != null).Select(x => x.Error)));
            return Ok(results, msg);
        }

        private void CreateRun(RacewayRunInfo run, RacewayRunResult r)
        {
            if (run.Points == null || run.Points.Count < 2) { r.Error = "A run needs at least 2 points."; return; }

            ElementType type = ResolveType(run, out string err);
            if (type == null) { r.Error = err; return; }
            r.TypeName = type.Name;

            var pts = run.Points.Select(ElectricalUtils.ToXYZ).ToList();
            Level level = ElectricalUtils.ResolveLevel(Doc, run.LevelName, pts[0].Z);
            if (level == null) { r.Error = "The project has no levels."; return; }

            var segments = new List<MEPCurve>();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (pts[i].DistanceTo(pts[i + 1]) < 1e-6) { Warnings.Add($"{run.Name}: skipped zero-length segment {i}."); continue; }
                MEPCurve seg = _kind == RacewayKind.CableTray
                    ? (MEPCurve)CableTray.Create(Doc, type.Id, pts[i], pts[i + 1], level.Id)
                    : Conduit.Create(Doc, type.Id, pts[i], pts[i + 1], level.Id);
                if (seg == null) { Warnings.Add($"{run.Name}: segment {i} was not created."); continue; }
                ApplySize(seg, run);
                segments.Add(seg);
                r.SegmentIds.Add(seg.Id.GetValue());
            }

            if (run.AddFittings && segments.Count > 1)
            {
                Doc.Regenerate();
                for (int i = 0; i < segments.Count - 1; i++)
                {
                    XYZ joint = pts[i + 1];
                    var c1 = ElectricalUtils.FindConnectorAt(segments[i].ConnectorManager, joint, 0.05);
                    var c2 = ElectricalUtils.FindConnectorAt(segments[i + 1].ConnectorManager, joint, 0.05);
                    if (c1 == null || c2 == null) { Warnings.Add($"{run.Name}: connectors not found at joint {i + 1}."); continue; }
                    try
                    {
                        var fitting = Doc.Create.NewElbowFitting(c1, c2);
                        if (fitting != null) r.FittingIds.Add(fitting.Id.GetValue());
                    }
                    catch (Exception ex)
                    {
                        // Typical cause: no elbow fitting family assigned to the type (Architectural template).
                        Warnings.Add($"{run.Name}: elbow at joint {i + 1} failed: {ex.Message}");
                    }
                }
            }
        }

        private ElementType ResolveType(RacewayRunInfo run, out string error)
        {
            error = null;
            Type cls = _kind == RacewayKind.CableTray ? typeof(CableTrayType) : typeof(ConduitType);
            if (run.TypeId > 0)
            {
                var t = Doc.GetElement(ElectricalUtils.ToElementId(run.TypeId)) as ElementType;
                if (t != null && cls.IsInstanceOfType(t)) return t;
                error = $"typeId {run.TypeId} is not a {cls.Name}.";
                return null;
            }
            var all = new FilteredElementCollector(Doc).OfClass(cls).Cast<ElementType>().OrderBy(t => t.Name).ToList();
            if (all.Count == 0)
            {
                error = $"The project has no {cls.Name}. Start from an Electrical template or transfer project standards (Manage > Transfer Project Standards) from one.";
                return null;
            }
            if (!string.IsNullOrWhiteSpace(run.TypeName))
            {
                var match = all.FirstOrDefault(t => t.Name.IndexOf(run.TypeName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
                Warnings.Add($"{run.Name}: no {cls.Name} named like '{run.TypeName}', used '{all[0].Name}'.");
            }
            return all[0];
        }

        private void ApplySize(MEPCurve seg, RacewayRunInfo run)
        {
            if (_kind == RacewayKind.CableTray)
            {
                SetLength(seg, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM, run.Width);
                SetLength(seg, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM, run.Height);
            }
            else
            {
                SetLength(seg, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM, run.Diameter);
            }
        }

        private void SetLength(Element e, BuiltInParameter bip, double mm)
        {
            if (mm <= 0) return;
            var p = e.get_Parameter(bip);
            if (p == null || p.IsReadOnly) return;
            try { p.Set(ElectricalUtils.MmToFt(mm)); }
            catch (Exception ex) { Warnings.Add($"Could not set {bip} = {mm} mm: {ex.Message}"); }
        }
    }
}
