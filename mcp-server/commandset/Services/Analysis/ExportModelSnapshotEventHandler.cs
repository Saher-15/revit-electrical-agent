using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Newtonsoft.Json;
using RevitMCPCommandSet.Models.Analysis;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Analysis
{
    /// <summary>
    /// Read-only export of the whole model (levels, rooms incl. linked rooms, spaces,
    /// electrical elements, raceways, panels and circuits) to a JSON file on disk.
    /// The socket only carries a short summary, so large models never flood the client.
    /// </summary>
    public class ExportModelSnapshotEventHandler : ElectricalHandlerBase<ModelSnapshotRequest, ModelSnapshotResult>
    {
        public override string GetName() => "export_model_snapshot";

        private static readonly string[] InstanceParameterNames =
        {
            "Mark", "Comments", "Elevation from Level", "Offset from Host", "Switch ID", "Size",
            "Apparent Load", "Voltage", "Number of Poles", "Load Classification", "Wattage",
        };

        private static readonly string[] TypeParameterNames =
        {
            "Type Mark", "Description", "Manufacturer", "Model", "Assembly Code", "Assembly Description",
            "Keynote", "Cost", "Type Comments", "Wattage", "Apparent Load", "Voltage", "Number of Poles",
            "Load Classification", "Power Factor", "Lamp", "Initial Intensity", "Light Loss Factor",
        };

        private static readonly string[] PanelParameterNames =
        {
            "Mains", "MCB Rating", "Max Number of Circuits", "Max #1 Pole Breakers", "Enclosure", "Mounting",
            "Short Circuit Rating", "Total Connected", "Total Demand", "Total Connected Current", "Total Estimated Demand Current",
            "Supply From", "Panel Name", "Location", "Mark", "Comments",
        };

        private static readonly string[] CircuitParameterNames =
        {
            "Apparent Load Phase A", "Apparent Load Phase B", "Apparent Load Phase C", "Voltage Drop", "Wire Type",
            "Number of Runs", "Number of Hot Conductors", "Number of Neutral Conductors", "Number of Ground Conductors",
            "Frame", "Schedule Circuit Notes", "Power Factor",
        };

        private readonly struct LinkRooms
        {
            public LinkRooms(RevitLinkInstance instance, Document doc, Transform transform)
            {
                Instance = instance; Doc = doc; Transform = transform; Inverse = transform.Inverse;
            }
            public RevitLinkInstance Instance { get; }
            public Document Doc { get; }
            public Transform Transform { get; }
            public Transform Inverse { get; }
        }

        private readonly List<LinkRooms> _links = new List<LinkRooms>();
        private readonly Dictionary<long, string> _levelNames = new Dictionary<long, string>();
        private readonly Dictionary<long, double> _levelElevFt = new Dictionary<long, double>();
        private ModelSnapshotRequest _req;

        protected override AIResult<ModelSnapshotResult> Run()
        {
            var watch = Stopwatch.StartNew();
            _req = Input ?? new ModelSnapshotRequest();
            _links.Clear();
            _levelNames.Clear();
            _levelElevFt.Clear();

            var snap = new ModelSnapshot
            {
                ExportedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                RevitVersion = UiApp.Application.VersionNumber + " " + UiApp.Application.SubVersionNumber,
            };

            ReadDocument(snap);
            ReadLevels(snap);
            ReadCategoryCounts(snap);
            ReadRooms(snap);
            if (_req.IncludeArchitecture) ReadArchitecture(snap);
            ReadElements(snap);
            ReadRaceways(snap);
            ReadPanelsAndCircuits(snap);
            snap.Warnings.AddRange(Warnings);

            string path = ResolveOutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var sw = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
            using (var jw = new JsonTextWriter(sw) { Formatting = Formatting.None })
            {
                new JsonSerializer().Serialize(jw, snap);
            }

            watch.Stop();
            var result = new ModelSnapshotResult
            {
                FilePath = path,
                FileSizeKb = new FileInfo(path).Length / 1024,
                DocumentTitle = snap.Document.Title,
                LevelCount = snap.Levels.Count,
                LinkCount = snap.Links.Count,
                RoomCount = snap.Rooms.Count(r => r.Kind == "Room"),
                SpaceCount = snap.Rooms.Count(r => r.Kind == "Space"),
                ElementCount = snap.Elements.Count,
                RacewayCount = snap.Raceways.Count,
                PanelCount = snap.Panels.Count,
                CircuitCount = snap.Panels.Sum(p => p.Circuits.Count) + snap.CircuitsWithoutPanel.Count,
                ElementsWithoutRoom = snap.Elements.Count(e => e.RoomKey == null && e.SpaceKey == null),
                FurnitureCount = snap.Furniture.Count,
                WindowCount = snap.Windows.Count,
                WallCount = snap.Walls.Count,
                Seconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
            };
            return Ok(result, $"Snapshot of '{result.DocumentTitle}' written to {path} ({result.FileSizeKb} KB): " +
                              $"{result.RoomCount} rooms, {result.SpaceCount} spaces, {result.ElementCount} elements, " +
                              $"{result.RacewayCount} raceway segments, {result.PanelCount} panels, {result.CircuitCount} circuits, " +
                              $"{result.FurnitureCount} furniture/fixtures, {result.WindowCount} windows, {result.WallCount} walls.");
        }

        // ---------------- architecture for the pattern engine (schemaVersion 2) ----------------

        private static readonly BuiltInCategory[] FurnitureCategories =
        {
            BuiltInCategory.OST_Furniture, BuiltInCategory.OST_FurnitureSystems, BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_GenericModel,
        };

        /// <summary>Furniture, windows and walls from the host model and every loaded link, in host coordinates (mm).</summary>
        private void ReadArchitecture(ModelSnapshot snap)
        {
            AddArchitecture(snap, Doc, null, Transform.Identity);
            foreach (var link in _links)
                AddArchitecture(snap, link.Doc, link.Instance, link.Transform);
        }

        private void AddArchitecture(ModelSnapshot snap, Document doc, RevitLinkInstance link, Transform transform)
        {
            bool isHost = link == null;
            string source = isHost ? "host" : link.Name;

            foreach (var bic in FurnitureCategories)
            {
                // The host model's own generic models and mechanical equipment are usually ours, not the architect's.
                if (isHost && (bic == BuiltInCategory.OST_GenericModel || bic == BuiltInCategory.OST_MechanicalEquipment)) continue;
                IEnumerable<FamilyInstance> items;
                try { items = new FilteredElementCollector(doc).OfCategory(bic).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList(); }
                catch { continue; }
                foreach (var fi in items)
                {
                    try
                    {
                        var bb = fi.get_BoundingBox(null);
                        if (bb == null) continue;
                        TransformBox(bb, transform, out XYZ min, out XYZ max);
                        var center = (min + max) / 2;
                        var level = doc.GetElement(fi.LevelId) as Level;
                        if (!LevelMatches(level?.Name, min.Z, isHost)) continue;
                        // Skip tiny or huge things: hardware, and whole-floor generic models.
                        double sizeX = max.X - min.X, sizeY = max.Y - min.Y;
                        if (Math.Max(sizeX, sizeY) < ElectricalUtils.MmToFt(150) || Math.Min(sizeX, sizeY) > ElectricalUtils.MmToFt(15000)) continue;

                        var lp = fi.Location as LocationPoint;
                        var p = lp != null ? transform.OfPoint(lp.Point) : center;
                        var f = new SnapshotFurniture
                        {
                            Id = fi.Id.GetValue(),
                            Source = source,
                            Category = fi.Category?.Name,
                            BuiltInCategory = bic.ToString(),
                            Family = fi.Symbol?.FamilyName,
                            Type = fi.Symbol?.Name,
                            Level = level?.Name,
                            XyzMm = Pt(p),
                            BboxMinMm = Pt(min),
                            BboxMaxMm = Pt(max),
                            HostId = fi.Host?.Id.GetValue(),
                        };
                        try { var facing = transform.OfVector(fi.FacingOrientation); f.Facing = new[] { Math.Round(facing.X, 4), Math.Round(facing.Y, 4) }; } catch { }
                        // Room at the footprint centre, 500 mm above the bottom (wardrobes touch the ceiling, beds sit on the floor).
                        FindRoom(new XYZ(center.X, center.Y, min.Z + ElectricalUtils.MmToFt(500)), min.Z, out string roomKey, out _);
                        f.RoomKey = roomKey;
                        snap.Furniture.Add(f);
                    }
                    catch (Exception ex) { Warnings.Add($"Furniture {fi.Id.GetValue()} skipped: {ex.Message}"); }
                }
            }

            IEnumerable<FamilyInstance> windows;
            try { windows = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Windows).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList(); }
            catch { windows = Enumerable.Empty<FamilyInstance>(); }
            foreach (var w in windows)
            {
                try
                {
                    if (!(w.Location is LocationPoint lp)) continue;
                    var p = transform.OfPoint(lp.Point);
                    var level = doc.GetElement(w.LevelId) as Level;
                    if (!LevelMatches(level?.Name, p.Z, isHost)) continue;
                    double widthFt = w.get_Parameter(BuiltInParameter.WINDOW_WIDTH)?.AsDouble() ?? 0;
                    if (widthFt <= 0) widthFt = w.Symbol?.get_Parameter(BuiltInParameter.WINDOW_WIDTH)?.AsDouble() ?? 0;
                    if (widthFt <= 0) widthFt = w.Symbol?.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM)?.AsDouble() ?? 0;
                    double heightFt = w.get_Parameter(BuiltInParameter.WINDOW_HEIGHT)?.AsDouble() ?? 0;
                    if (heightFt <= 0) heightFt = w.Symbol?.get_Parameter(BuiltInParameter.WINDOW_HEIGHT)?.AsDouble() ?? 0;
                    if (heightFt <= 0) heightFt = w.Symbol?.get_Parameter(BuiltInParameter.FAMILY_HEIGHT_PARAM)?.AsDouble() ?? 0;
                    double sillFt = w.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.AsDouble() ?? 0;
                    double levelZ = (level?.Elevation ?? 0) + transform.Origin.Z;
                    var hand = transform.OfVector(w.HandOrientation);
                    var facing = transform.OfVector(w.FacingOrientation);
                    var sw = new SnapshotWindow
                    {
                        Id = w.Id.GetValue(),
                        Source = source,
                        Level = level?.Name,
                        Family = w.Symbol?.FamilyName,
                        Type = w.Symbol?.Name,
                        XyzMm = Pt(new XYZ(p.X, p.Y, levelZ + sillFt)),
                        WidthMm = Mm(widthFt),
                        SillMm = Mm(sillFt),
                        HeadMm = Mm(sillFt + heightFt),
                        AlongWall = new[] { Math.Round(hand.X, 4), Math.Round(hand.Y, 4) },
                        Facing = new[] { Math.Round(facing.X, 4), Math.Round(facing.Y, 4) },
                    };
                    // Rooms on both sides of the wall, 300 mm in, 1 m above the level.
                    var keys = new List<string>();
                    double inFt = ElectricalUtils.MmToFt(300);
                    foreach (var side in new[] { facing, -facing })
                    {
                        FindRoom(new XYZ(p.X + side.X * inFt, p.Y + side.Y * inFt, levelZ + ElectricalUtils.MmToFt(1000)), levelZ, out string rk, out _);
                        if (rk != null && !keys.Contains(rk)) keys.Add(rk);
                    }
                    if (keys.Count > 0) sw.RoomKeys = keys;
                    snap.Windows.Add(sw);
                }
                catch (Exception ex) { Warnings.Add($"Window {w.Id.GetValue()} skipped: {ex.Message}"); }
            }

            IEnumerable<Wall> walls;
            try { walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList(); }
            catch { walls = Enumerable.Empty<Wall>(); }
            foreach (var wall in walls)
            {
                try
                {
                    if (!(wall.Location is LocationCurve lc)) continue;
                    var a = transform.OfPoint(lc.Curve.GetEndPoint(0));
                    var b = transform.OfPoint(lc.Curve.GetEndPoint(1));
                    var level = doc.GetElement(wall.LevelId) as Level;
                    if (!LevelMatches(level?.Name, Math.Min(a.Z, b.Z), isHost)) continue;
                    var sw = new SnapshotWall
                    {
                        Id = wall.Id.GetValue(),
                        Source = source,
                        Level = level?.Name,
                        Type = wall.WallType?.Name ?? wall.Name,
                        StartMm = Pt(a),
                        EndMm = Pt(b),
                        ThicknessMm = Mm(wall.Width),
                    };
                    var h = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble();
                    if (h.HasValue && h.Value > 0) sw.HeightMm = Mm(h.Value);
                    try { sw.Function = wall.WallType?.Function.ToString(); } catch { }
                    if (wall.CurtainGrid != null) sw.Function = "Curtain";
                    snap.Walls.Add(sw);
                }
                catch (Exception ex) { Warnings.Add($"Wall {wall.Id.GetValue()} skipped: {ex.Message}"); }
            }
        }

        private static void TransformBox(BoundingBoxXYZ bb, Transform transform, out XYZ min, out XYZ max)
        {
            var t = transform.Multiply(bb.Transform);
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var x in new[] { bb.Min.X, bb.Max.X })
            foreach (var y in new[] { bb.Min.Y, bb.Max.Y })
            foreach (var z in new[] { bb.Min.Z, bb.Max.Z })
            {
                var c = t.OfPoint(new XYZ(x, y, z));
                minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y); minZ = Math.Min(minZ, c.Z);
                maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y); maxZ = Math.Max(maxZ, c.Z);
            }
            min = new XYZ(minX, minY, minZ);
            max = new XYZ(maxX, maxY, maxZ);
        }

        // ---------------- document, levels, counts ----------------

        private void ReadDocument(ModelSnapshot snap)
        {
            snap.Document.Title = Doc.Title;
            snap.Document.Path = Doc.PathName;
            snap.Document.IsWorkshared = Doc.IsWorkshared;
            snap.Document.ActiveView = Doc.ActiveView?.Name;
            var info = Doc.ProjectInformation;
            if (info == null) return;
            foreach (Parameter p in info.Parameters)
            {
                string v = ParamValue(p);
                if (!string.IsNullOrWhiteSpace(v) && p.Definition != null)
                    snap.Document.ProjectInfo[p.Definition.Name] = v;
            }
        }

        private void ReadLevels(ModelSnapshot snap)
        {
            foreach (var lvl in new FilteredElementCollector(Doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
            {
                _levelNames[lvl.Id.GetValue()] = lvl.Name;
                _levelElevFt[lvl.Id.GetValue()] = lvl.Elevation;
                snap.Levels.Add(new SnapshotLevel { Id = lvl.Id.GetValue(), Name = lvl.Name, ElevationMm = Mm(lvl.Elevation) });
            }
        }

        private void ReadCategoryCounts(ModelSnapshot snap)
        {
            var counts = new Dictionary<string, int>();
            foreach (var e in new FilteredElementCollector(Doc).WhereElementIsNotElementType())
            {
                var cat = e.Category;
                if (cat == null || cat.CategoryType != CategoryType.Model) continue;
                counts[cat.Name] = counts.TryGetValue(cat.Name, out int n) ? n + 1 : 1;
            }
            foreach (var kv in counts.OrderByDescending(kv => kv.Value))
                snap.CategoryCounts[kv.Key] = kv.Value;
        }

        // ---------------- rooms and spaces ----------------

        private void ReadRooms(ModelSnapshot snap)
        {
            foreach (var room in new FilteredElementCollector(Doc).OfCategory(BuiltInCategory.OST_Rooms).OfType<Room>())
                AddSpatial(snap, room, "Room", "host", RoomKey(null, room), Transform.Identity);
            foreach (var space in new FilteredElementCollector(Doc).OfCategory(BuiltInCategory.OST_MEPSpaces).OfType<Space>())
                AddSpatial(snap, space, "Space", "host", SpaceKey(space), Transform.Identity);
            AddDoors(snap, Doc, null, Transform.Identity);

            foreach (var inst in new FilteredElementCollector(Doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                var linkDoc = inst.GetLinkDocument();
                var link = new SnapshotLink { Id = inst.Id.GetValue(), Name = inst.Name, Loaded = linkDoc != null, DocumentTitle = linkDoc?.Title };
                snap.Links.Add(link);
                if (linkDoc == null)
                {
                    Warnings.Add($"Link '{inst.Name}' is not loaded; its rooms are missing.");
                    continue;
                }
                var lr = new LinkRooms(inst, linkDoc, inst.GetTotalTransform());
                _links.Add(lr);
                if (!_req.IncludeLinkedRooms) continue;
                int before = snap.Rooms.Count;
                foreach (var room in new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_Rooms).OfType<Room>())
                    AddSpatial(snap, room, "Room", inst.Name, RoomKey(inst, room), lr.Transform);
                link.RoomCount = snap.Rooms.Count - before;
                AddDoors(snap, linkDoc, inst, lr.Transform);
            }
        }

        private void AddDoors(ModelSnapshot snap, Document doc, RevitLinkInstance link, Transform transform)
        {
            foreach (var door in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                try
                {
                    if (!(door.Location is LocationPoint lp)) continue;
                    var p = transform.OfPoint(lp.Point);
                    var level = doc.GetElement(door.LevelId) as Level;
                    if (!LevelMatches(level?.Name, p.Z, link == null)) continue;
                    double widthFt = door.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsDouble() ?? 0;
                    if (widthFt <= 0) widthFt = door.Symbol?.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsDouble() ?? 0;
                    if (widthFt <= 0) widthFt = door.Symbol?.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM)?.AsDouble() ?? 0;
                    var hand = transform.OfVector(door.HandOrientation);
                    var facing = transform.OfVector(door.FacingOrientation);
                    var d = new SnapshotDoor
                    {
                        Id = door.Id.GetValue(),
                        Source = link == null ? "host" : link.Name,
                        Level = level?.Name,
                        Family = door.Symbol?.FamilyName,
                        Type = door.Symbol?.Name,
                        XyzMm = Pt(p),
                        WidthMm = Mm(widthFt),
                        AlongWall = new[] { Math.Round(hand.X, 4), Math.Round(hand.Y, 4) },
                        Facing = new[] { Math.Round(facing.X, 4), Math.Round(facing.Y, 4) },
                    };
                    if (door.Host is Wall wall) d.WallThicknessMm = Mm(wall.Width);
                    try { if (door.FromRoom != null) d.FromRoomKey = RoomKey(link, door.FromRoom); } catch { }
                    try { if (door.ToRoom != null) d.ToRoomKey = RoomKey(link, door.ToRoom); } catch { }
                    snap.Doors.Add(d);
                }
                catch (Exception ex) { Warnings.Add($"Door {door.Id.GetValue()} skipped: {ex.Message}"); }
            }
        }

        private void AddSpatial(ModelSnapshot snap, SpatialElement se, string kind, string source, string key, Transform transform)
        {
            if (se.Area <= 0) return; // unplaced, unenclosed or redundant
            var level = se.Level;
            double baseFt = (level?.Elevation ?? 0) + transform.Origin.Z;
            if (se is Room r0) baseFt += r0.BaseOffset;
            if (!LevelMatches(level?.Name, baseFt, source == "host")) return;

            var s = new SnapshotRoom
            {
                Key = key,
                Kind = kind,
                Source = source,
                Id = se.Id.GetValue(),
                Number = se.Number,
                Name = se.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? se.Name,
                Level = level?.Name,
                Department = se.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString(),
                AreaM2 = Math.Round(UnitUtils.ConvertFromInternalUnits(se.Area, UnitTypeId.SquareMeters), 2),
                PerimeterM = Math.Round(UnitUtils.ConvertFromInternalUnits(se.Perimeter, UnitTypeId.Meters), 2),
                BaseElevationMm = Mm(baseFt),
            };
            if (se is Room room)
            {
                s.VolumeM3 = Math.Round(UnitUtils.ConvertFromInternalUnits(room.Volume, UnitTypeId.CubicMeters), 2);
                s.HeightMm = Mm(room.UnboundedHeight);
            }
            else if (se is Space space)
            {
                s.VolumeM3 = Math.Round(UnitUtils.ConvertFromInternalUnits(space.Volume, UnitTypeId.CubicMeters), 2);
                s.HeightMm = Mm(space.UnboundedHeight);
            }
            if (se.Location is LocationPoint lp) s.CenterMm = Pt(transform.OfPoint(lp.Point));

            if (_req.IncludeRoomBoundaries)
            {
                try
                {
                    var loops = se.GetBoundarySegments(new SpatialElementBoundaryOptions());
                    var outer = loops?.FirstOrDefault();
                    if (outer != null)
                    {
                        s.BoundaryMm = new List<double[]>();
                        foreach (var seg in outer)
                        {
                            var pts = seg.GetCurve().Tessellate();
                            for (int i = 0; i < pts.Count - 1; i++)
                            {
                                var p = transform.OfPoint(pts[i]);
                                s.BoundaryMm.Add(new[] { Math.Round(Mm(p.X)), Math.Round(Mm(p.Y)) });
                            }
                        }
                    }
                }
                catch (Exception ex) { Warnings.Add($"Boundary of {kind} {se.Number} ({source}) skipped: {ex.Message}"); }
            }

            if (_req.IncludeAllParameters) s.Parameters = AllParameters(se, null);
            snap.Rooms.Add(s);
        }

        // ---------------- elements ----------------

        private void ReadElements(ModelSnapshot snap)
        {
            var categories = ElectricalUtils.ElectricalCategories
                .Where(c => c != BuiltInCategory.OST_ElectricalCircuit && c != BuiltInCategory.OST_CableTray &&
                            c != BuiltInCategory.OST_Conduit && c != BuiltInCategory.OST_Wire)
                .ToList();
            foreach (var name in _req.ExtraCategories ?? new List<string>())
            {
                if (ElectricalUtils.TryParseCategory(name, out var bic)) { if (!categories.Contains(bic)) categories.Add(bic); }
                else Warnings.Add($"Unknown category '{name}' ignored.");
            }

            foreach (var bic in categories)
            {
                IEnumerable<Element> elements;
                try { elements = new FilteredElementCollector(Doc).OfCategory(bic).WhereElementIsNotElementType().ToElements(); }
                catch { continue; }
                foreach (var e in elements)
                {
                    try { AddElement(snap, e, bic); }
                    catch (Exception ex) { Warnings.Add($"Element {e.Id.GetValue()} skipped: {ex.Message}"); }
                }
            }
        }

        private void AddElement(ModelSnapshot snap, Element e, BuiltInCategory bic)
        {
            var levelId = LevelIdOf(e);
            string levelName = levelId != null && _levelNames.TryGetValue(levelId.Value, out var ln) ? ln : null;
            var point = PointOf(e);
            if (!LevelMatches(levelName, point?.Z ?? double.NaN, true)) return;

            var type = Doc.GetElement(e.GetTypeId()) as ElementType;
            var s = new SnapshotElement
            {
                Id = e.Id.GetValue(),
                Category = e.Category?.Name,
                BuiltInCategory = bic.ToString(),
                Family = (type as FamilySymbol)?.FamilyName ?? type?.FamilyName,
                Type = type?.Name ?? e.Name,
                TypeId = type?.Id.GetValue() ?? -1,
                Level = levelName,
            };
            if (point != null)
            {
                s.XyzMm = Pt(point);
                if (levelId != null && _levelElevFt.TryGetValue(levelId.Value, out double lev))
                    s.HeightAboveLevelMm = Mm(point.Z - lev);
                double levelFt = levelId != null && _levelElevFt.TryGetValue(levelId.Value, out double le) ? le : point.Z;
                FindRoom(point, levelFt, out string roomKey, out string spaceKey);
                s.RoomKey = roomKey;
                s.SpaceKey = spaceKey;
            }

            if (e is FamilyInstance fi)
            {
                if (fi.Host != null)
                {
                    s.HostId = fi.Host.Id.GetValue();
                    s.HostCategory = fi.Host is RevitLinkInstance ? "Link: " + fi.Host.Name : fi.Host.Category?.Name;
                }
                ReadCircuitMembership(fi, s);
            }

            s.Parameters = _req.IncludeAllParameters ? AllParameters(e, type) : CuratedParameters(e, type);
            snap.Elements.Add(s);
        }

        private static void ReadCircuitMembership(FamilyInstance fi, SnapshotElement s)
        {
            var mep = fi.MEPModel;
            var cm = mep?.ConnectorManager;
            if (cm == null) return;
            bool hasElectrical = false;
            foreach (Connector c in cm.Connectors)
                if (c.Domain == Domain.DomainElectrical) { hasElectrical = true; break; }
            if (!hasElectrical) return;

            ISet<ElectricalSystem> systems = null;
            try { systems = mep.GetElectricalSystems(); } catch { }
            // A panel lists the circuits it feeds among its systems; keep only the one feeding it.
            var feeding = systems?.Where(sys => sys.BaseEquipment == null || sys.BaseEquipment.Id != fi.Id).ToList();
            if (feeding == null || feeding.Count == 0)
            {
                if (!(mep is ElectricalEquipment)) s.Unassigned = true;
                return;
            }
            s.Panel = string.Join(", ", feeding.Select(x => x.PanelName).Distinct());
            s.CircuitNumber = string.Join(", ", feeding.Select(x => x.CircuitNumber));
        }

        // ---------------- raceways ----------------

        private void ReadRaceways(ModelSnapshot snap)
        {
            foreach (var bic in new[] { BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit })
            {
                foreach (var curve in new FilteredElementCollector(Doc).OfCategory(bic).WhereElementIsNotElementType().OfType<MEPCurve>())
                {
                    try
                    {
                        if (!(curve.Location is LocationCurve lc)) continue;
                        var start = lc.Curve.GetEndPoint(0);
                        var end = lc.Curve.GetEndPoint(1);
                        string levelName = curve.ReferenceLevel?.Name;
                        if (!LevelMatches(levelName, Math.Min(start.Z, end.Z), true)) continue;

                        var type = Doc.GetElement(curve.GetTypeId()) as ElementType;
                        var r = new SnapshotRaceway
                        {
                            Id = curve.Id.GetValue(),
                            Category = curve.Category?.Name,
                            Type = type?.Name,
                            TypeId = type?.Id.GetValue() ?? -1,
                            Level = levelName,
                            LengthM = Math.Round(UnitUtils.ConvertFromInternalUnits(lc.Curve.Length, UnitTypeId.Meters), 3),
                            Size = curve.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString(),
                            ServiceType = curve.LookupParameter("Service Type")?.AsString(),
                            StartMm = Pt(start),
                            EndMm = Pt(end),
                        };
                        if (bic == BuiltInCategory.OST_CableTray)
                        {
                            try { r.WidthMm = Mm(curve.Width); } catch { }
                            try { r.HeightMm = Mm(curve.Height); } catch { }
                        }
                        else
                        {
                            try { r.DiameterMm = Mm(curve.Diameter); } catch { }
                        }
                        var mid = lc.Curve.Evaluate(0.5, true);
                        double levelFt = curve.ReferenceLevel?.Elevation ?? mid.Z;
                        FindRoom(mid, levelFt, out string roomKey, out string spaceKey);
                        r.RoomKey = roomKey ?? spaceKey;
                        if (_req.IncludeAllParameters) r.Parameters = AllParameters(curve, type);
                        snap.Raceways.Add(r);
                    }
                    catch (Exception ex) { Warnings.Add($"Raceway {curve.Id.GetValue()} skipped: {ex.Message}"); }
                }
            }
        }

        // ---------------- panels, circuits, distribution systems ----------------

        private void ReadPanelsAndCircuits(ModelSnapshot snap)
        {
            foreach (var ds in new FilteredElementCollector(Doc).OfClass(typeof(DistributionSysType)).Cast<DistributionSysType>())
            {
                var d = new SnapshotDistributionSystem { Id = ds.Id.GetValue(), Name = ds.Name };
                try { d.Phase = ds.ElectricalPhase.ToString(); } catch { }
                try { d.Wires = ds.NumWires; } catch { }
                try { if (ds.VoltageLineToLine != null) d.LineToLineV = Math.Round(ElectricalUtils.ToVolts(ds.VoltageLineToLine.ActualValue), 1); } catch { }
                try { if (ds.VoltageLineToGround != null) d.LineToGroundV = Math.Round(ElectricalUtils.ToVolts(ds.VoltageLineToGround.ActualValue), 1); } catch { }
                snap.DistributionSystems.Add(d);
            }

            var panelById = new Dictionary<long, SnapshotPanel>();
            var panels = new FilteredElementCollector(Doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.MEPModel is ElectricalEquipment);
            foreach (var fi in panels)
            {
                var levelId = LevelIdOf(fi);
                string levelName = levelId != null && _levelNames.TryGetValue(levelId.Value, out var ln) ? ln : null;
                var point = PointOf(fi);
                var p = new SnapshotPanel
                {
                    Id = fi.Id.GetValue(),
                    Name = fi.Name,
                    Family = fi.Symbol.FamilyName,
                    Type = fi.Symbol.Name,
                    Level = levelName,
                };
                try { p.DistributionSystem = ((ElectricalEquipment)fi.MEPModel).DistributionSystem?.Name; } catch { }
                if (point != null)
                {
                    double levelFt = levelId != null && _levelElevFt.TryGetValue(levelId.Value, out double le) ? le : point.Z;
                    FindRoom(point, levelFt, out string roomKey, out string spaceKey);
                    p.RoomKey = roomKey ?? spaceKey;
                }
                try
                {
                    var feeders = fi.MEPModel.GetElectricalSystems()?.Where(sys => sys.BaseEquipment != null && sys.BaseEquipment.Id != fi.Id);
                    p.FedFrom = feeders == null ? null : string.Join(", ", feeders.Select(x => $"{x.PanelName} / {x.CircuitNumber}"));
                    if (string.IsNullOrEmpty(p.FedFrom)) p.FedFrom = null;
                }
                catch { }
                p.Parameters = NamedParameters(fi, fi.Symbol, PanelParameterNames);
                panelById[p.Id] = p;
                if (LevelMatches(levelName, point?.Z ?? double.NaN, true)) snap.Panels.Add(p);
            }

            foreach (var sys in new FilteredElementCollector(Doc).OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
            {
                var c = ToCircuit(sys);
                long baseId = sys.BaseEquipment?.Id.GetValue() ?? -1;
                if (baseId >= 0 && panelById.TryGetValue(baseId, out var panel))
                {
                    panel.Circuits.Add(c);
                    panel.TotalApparentLoadVA += c.ApparentLoadVA;
                }
                else if (_req.LevelName == null)
                {
                    snap.CircuitsWithoutPanel.Add(c);
                }
            }
            foreach (var p in snap.Panels)
            {
                p.TotalApparentLoadVA = Math.Round(p.TotalApparentLoadVA, 1);
                p.Circuits = p.Circuits.OrderBy(c => c.StartSlot).ThenBy(c => c.CircuitNumber).ToList();
            }
        }

        private SnapshotCircuit ToCircuit(ElectricalSystem sys)
        {
            var c = new SnapshotCircuit
            {
                Id = sys.Id.GetValue(),
                CircuitNumber = sys.CircuitNumber,
                Panel = sys.PanelName,
                SystemType = sys.SystemType.ToString(),
                LoadName = sys.LoadName,
            };
            try { c.Voltage = Math.Round(ElectricalUtils.ToVolts(sys.Voltage), 1); } catch { }
            try { c.ApparentLoadVA = Math.Round(ElectricalUtils.ToVoltAmperes(sys.ApparentLoad), 1); } catch { }
            try { c.TrueLoadW = Math.Round(UnitUtils.ConvertFromInternalUnits(sys.TrueLoad, UnitTypeId.Watts), 1); } catch { }
            try { c.Poles = sys.PolesNumber; } catch { }
            try { c.RatingA = Math.Round(UnitUtils.ConvertFromInternalUnits(sys.Rating, UnitTypeId.Amperes), 1); } catch { }
            try { c.StartSlot = sys.StartSlot; } catch { }
            try { c.LengthM = Math.Round(UnitUtils.ConvertFromInternalUnits(sys.Length, UnitTypeId.Meters), 2); } catch { }
            try { c.WireSize = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM)?.AsString(); } catch { }
            if (sys.Elements != null)
            {
                foreach (Element e in sys.Elements)
                {
                    c.ElementIds.Add(e.Id.GetValue());
                    if (e is FamilyInstance fi && fi.MEPModel is ElectricalEquipment) c.FeedsPanel = fi.Name;
                }
            }
            var prm = NamedParameters(sys, null, CircuitParameterNames);
            if (prm.Count > 0) c.Parameters = prm;
            return c;
        }

        // ---------------- room lookup ----------------

        private void FindRoom(XYZ point, double levelElevFt, out string roomKey, out string spaceKey)
        {
            roomKey = null;
            spaceKey = null;
            // Ceiling-mounted items often sit above the room's upper limit, so also probe 1 m above the level.
            var probes = new List<XYZ> { point };
            double probeZ = levelElevFt + ElectricalUtils.MmToFt(1000);
            if (Math.Abs(point.Z - probeZ) > 1e-6) probes.Add(new XYZ(point.X, point.Y, probeZ));

            foreach (var p in probes)
            {
                if (spaceKey == null)
                {
                    try { var sp = Doc.GetSpaceAtPoint(p); if (sp != null) spaceKey = SpaceKey(sp); } catch { }
                }
                if (roomKey == null)
                {
                    try { var r = Doc.GetRoomAtPoint(p); if (r != null) roomKey = RoomKey(null, r); } catch { }
                }
                if (roomKey == null)
                {
                    foreach (var link in _links)
                    {
                        try
                        {
                            var r = link.Doc.GetRoomAtPoint(link.Inverse.OfPoint(p));
                            if (r != null) { roomKey = RoomKey(link.Instance, r); break; }
                        }
                        catch { }
                    }
                }
                if (roomKey != null && spaceKey != null) return;
            }
        }

        private static string RoomKey(RevitLinkInstance link, Room room) =>
            link == null ? $"R:{room.Id.GetValue()}" : $"L{link.Id.GetValue()}:{room.Id.GetValue()}";

        private static string SpaceKey(Space space) => $"S:{space.Id.GetValue()}";

        // ---------------- helpers ----------------

        private bool LevelMatches(string levelName, double zFt, bool isHost)
        {
            if (string.IsNullOrWhiteSpace(_req.LevelName)) return true;
            if (isHost && levelName != null)
                return levelName.IndexOf(_req.LevelName, StringComparison.OrdinalIgnoreCase) >= 0;
            if (double.IsNaN(zFt)) return false;
            // Linked rooms or elements without a level: match by elevation band of the requested host level.
            var levels = _levelElevFt.OrderBy(kv => kv.Value).ToList();
            for (int i = 0; i < levels.Count; i++)
            {
                if (_levelNames[levels[i].Key].IndexOf(_req.LevelName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                double tol = ElectricalUtils.MmToFt(500);
                double lo = levels[i].Value - tol;
                double hi = i + 1 < levels.Count ? levels[i + 1].Value - tol : double.MaxValue;
                if (zFt >= lo && zFt < hi) return true;
            }
            return false;
        }

        internal static long? LevelIdOf(Element e)
        {
            if (e.LevelId != null && e.LevelId != ElementId.InvalidElementId) return e.LevelId.GetValue();
            if (e is MEPCurve mc && mc.ReferenceLevel != null) return mc.ReferenceLevel.Id.GetValue();
            foreach (var bip in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM })
            {
                var id = e.get_Parameter(bip)?.AsElementId();
                if (id != null && id != ElementId.InvalidElementId) return id.GetValue();
            }
            return null;
        }

        private XYZ PointOf(Element e)
        {
            switch (e.Location)
            {
                case LocationPoint lp: return lp.Point;
                case LocationCurve lc: return lc.Curve.Evaluate(0.5, true);
            }
            var bb = e.get_BoundingBox(null);
            return bb == null ? null : (bb.Min + bb.Max) / 2;
        }

        private static Dictionary<string, string> CuratedParameters(Element e, ElementType type)
        {
            var result = new Dictionary<string, string>();
            foreach (var name in InstanceParameterNames)
            {
                string v = ParamValue(e.LookupParameter(name));
                if (!string.IsNullOrWhiteSpace(v)) result[name] = v;
            }
            if (type != null)
            {
                foreach (var name in TypeParameterNames)
                {
                    if (result.ContainsKey(name)) continue;
                    string v = ParamValue(type.LookupParameter(name));
                    if (!string.IsNullOrWhiteSpace(v)) result[name] = v;
                }
            }
            return result;
        }

        private static Dictionary<string, string> NamedParameters(Element e, ElementType type, IEnumerable<string> names)
        {
            var result = new Dictionary<string, string>();
            foreach (var name in names)
            {
                string v = ParamValue(e.LookupParameter(name));
                if (string.IsNullOrWhiteSpace(v) && type != null) v = ParamValue(type.LookupParameter(name));
                if (!string.IsNullOrWhiteSpace(v)) result[name] = v;
            }
            return result;
        }

        private static Dictionary<string, string> AllParameters(Element e, ElementType type)
        {
            var result = new Dictionary<string, string>();
            foreach (Parameter p in e.Parameters)
            {
                string v = ParamValue(p);
                if (!string.IsNullOrWhiteSpace(v) && p.Definition != null) result[p.Definition.Name] = v;
            }
            if (type != null)
            {
                foreach (Parameter p in type.Parameters)
                {
                    string v = ParamValue(p);
                    if (!string.IsNullOrWhiteSpace(v) && p.Definition != null && !result.ContainsKey(p.Definition.Name))
                        result["type: " + p.Definition.Name] = v;
                }
            }
            return result;
        }

        private static string ParamValue(Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String: return p.AsString();
                    case StorageType.ElementId: return p.AsValueString();
                    default: return p.AsValueString();
                }
            }
            catch { return null; }
        }

        private string ResolveOutputPath()
        {
            string folder = string.IsNullOrWhiteSpace(_req.OutputFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RevitClaude", "snapshots")
                : Environment.ExpandEnvironmentVariables(_req.OutputFolder);
            string name = string.IsNullOrWhiteSpace(_req.FileName)
                ? $"{Doc.Title}_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                : _req.FileName;
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) name += ".json";
            return Path.Combine(folder, name);
        }

        private static double Mm(double ft) => Math.Round(ElectricalUtils.FtToMm(ft), 1);

        private static double[] Pt(XYZ p) => new[] { Mm(p.X), Mm(p.Y), Mm(p.Z) };
    }
}
