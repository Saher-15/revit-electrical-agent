using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Analysis;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>Copies a level's electrical instances straight up or down to other levels, keeping their height above the level.</summary>
    public class CopyLevelElementsEventHandler : ElectricalHandlerBase<CopyLevelRequest, CopyLevelResult>
    {
        private static readonly BuiltInCategory[] DefaultCategories =
        {
            BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices, BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_TelephoneDevices, BuiltInCategory.OST_SecurityDevices, BuiltInCategory.OST_DataDevices,
        };

        // 50 mm: two instances of one type this close on a floor are the same point.
        private const double SamePointFt = 50 / 304.8;

        public override string GetName() => "copy_level_elements";

        protected override AIResult<CopyLevelResult> Run()
        {
            var req = Input ?? new CopyLevelRequest();
            // Editing a live cloud model can borrow elements in the team's central model, so it takes an explicit yes.
            if (Doc.IsModelInCloud && !req.AllowCloudModel)
                return Fail("The open model is connected to the cloud. Reopen it detached, or pass allowCloudModel once the user has accepted working on it without Sync.");

            var levels = new FilteredElementCollector(Doc).OfClass(typeof(Level)).Cast<Level>().ToDictionary(l => l.Name);
            if (string.IsNullOrEmpty(req.SourceLevel) || !levels.TryGetValue(req.SourceLevel, out Level source))
                return Fail($"Source level '{req.SourceLevel}' not found.");
            if (req.TargetLevels == null || req.TargetLevels.Count == 0) return Fail("Give targetLevels.");
            foreach (string name in req.TargetLevels)
            {
                if (!levels.ContainsKey(name)) return Fail($"Target level '{name}' not found.");
                if (name == req.SourceLevel) return Fail("A target level is the same as the source level.");
            }

            var cats = new List<BuiltInCategory>();
            foreach (string c in req.Categories ?? new List<string>())
            {
                if (ElectricalUtils.TryParseCategory(c, out var bic)) cats.Add(bic);
                else return Fail($"Unknown category '{c}'.");
            }
            if (cats.Count == 0) cats.AddRange(DefaultCategories);

            var sourceElements = InstancesOn(source, cats);
            if (sourceElements.Count == 0) return Fail($"Level '{source.Name}' has no elements in these categories.");
            var result = new CopyLevelResult { DryRun = req.DryRun, SourceLevel = source.Name, SourceElements = sourceElements.Count };

            using (var group = new TransactionGroup(Doc, "MCP: copy level elements"))
            {
                group.Start();
                foreach (string name in req.TargetLevels)
                    result.Targets.Add(CopyTo(sourceElements, source, levels[name], cats));
                if (req.DryRun) group.RollBack(); else group.Assimilate();
            }

            string mode = req.DryRun ? "Dry run, nothing was kept" : "Copied";
            return Ok(result, $"{mode}: {sourceElements.Count} elements on '{source.Name}' to {req.TargetLevels.Count} level(s).");
        }

        private CopyLevelTargetResult CopyTo(List<FamilyInstance> sourceElements, Level source, Level target, List<BuiltInCategory> cats)
        {
            var r = new CopyLevelTargetResult { Level = target.Name };
            var existing = InstancesOn(target, cats);
            var toCopy = new List<FamilyInstance>();
            foreach (var e in sourceElements)
            {
                if (existing.Any(x => SamePoint(x, e))) r.SkippedAlreadyThere++;
                else toCopy.Add(e);
            }
            r.SourceWithHostFace = toCopy.Count(e => e.HostFace != null);
            if (toCopy.Count == 0) return r;

            using (var t = new Transaction(Doc, $"MCP: copy level {source.Name} to {target.Name}"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);
                var copies = ElementTransformUtils.CopyElements(Doc, toCopy.Select(e => e.Id).ToList(), new XYZ(0, 0, target.Elevation - source.Elevation))
                    .Select(id => Doc.GetElement(id)).OfType<FamilyInstance>().ToList();
                Doc.Regenerate();

                var heights = copies.ToDictionary(c => c.Id, c => PointOf(c)?.Z);
                foreach (var c in copies) SetLevel(c, target);
                Doc.Regenerate();

                // Changing the level can carry the offset along: put the element back where the copy landed.
                foreach (var c in copies)
                {
                    double? before = heights[c.Id], after = PointOf(c)?.Z;
                    if (before == null || after == null || Math.Abs(before.Value - after.Value) < 0.001) continue;
                    try { ElementTransformUtils.MoveElement(Doc, c.Id, new XYZ(0, 0, before.Value - after.Value)); }
                    catch (Exception ex) { r.Messages.Add($"{c.Id.GetValue()}: {ex.Message}"); }
                }
                Doc.Regenerate();

                r.Copied = copies.Count;
                r.OnTargetLevel = copies.Count(c => ExportModelSnapshotEventHandler.LevelIdOf(c) == target.Id.GetValue());
                r.WrongHeight = copies.Count(c => heights[c.Id] != null && Math.Abs(heights[c.Id].Value - (PointOf(c)?.Z ?? double.MaxValue)) > 0.01);
                r.CopiesWithHostFace = copies.Count(c => c.HostFace != null);

                var status = t.Commit();
                r.RolledBack = pre.RolledBack || status != TransactionStatus.Committed;
                r.Messages.AddRange(pre.Messages.GroupBy(m => m).Select(g => $"{g.Count()} x {g.Key}").Take(20));
            }
            return r;
        }

        private List<FamilyInstance> InstancesOn(Level level, List<BuiltInCategory> cats)
        {
            var all = new FilteredElementCollector(Doc).WherePasses(new ElementMulticategoryFilter(cats)).WhereElementIsNotElementType()
                .OfType<FamilyInstance>().Where(e => e.SuperComponent == null).ToList();
            var levelOf = all.ToDictionary(e => e.Id, e => ExportModelSnapshotEventHandler.LevelIdOf(e));

            // Elements hosted on linked structure can have no level: they belong to the floor their height falls in.
            // Only levels that hold elements bound the floor, so the empty helper levels (Level 1, Level 2) do not split it.
            var used = new HashSet<long>(levelOf.Values.Where(v => v != null).Select(v => v.Value));
            double top = new FilteredElementCollector(Doc).OfClass(typeof(Level)).Cast<Level>()
                .Where(l => used.Contains(l.Id.GetValue()) && l.Elevation > level.Elevation)
                .Select(l => l.Elevation).DefaultIfEmpty(double.MaxValue).Min();

            return all.Where(e =>
            {
                long? id = levelOf[e.Id];
                if (id != null) return id == level.Id.GetValue();
                double? z = PointOf(e)?.Z;
                return z != null && z >= level.Elevation && z < top;
            }).ToList();
        }

        private static bool SamePoint(FamilyInstance a, FamilyInstance b)
        {
            XYZ pa = PointOf(a), pb = PointOf(b);
            return a.Symbol.Id == b.Symbol.Id && pa != null && pb != null
                && Math.Abs(pa.X - pb.X) < SamePointFt && Math.Abs(pa.Y - pb.Y) < SamePointFt;
        }

        private static XYZ PointOf(FamilyInstance e) => (e.Location as LocationPoint)?.Point;

        private static void SetLevel(FamilyInstance e, Level level)
        {
            foreach (var bip in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM })
            {
                var p = e.get_Parameter(bip);
                if (p != null && !p.IsReadOnly && p.Set(level.Id)) return;
            }
        }
    }
}
