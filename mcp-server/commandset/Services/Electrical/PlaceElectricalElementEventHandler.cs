using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Electrical
{
    /// <summary>
    /// Places electrical family instances with a real host: ceiling face, wall
    /// face/wall, floor face, level, or a reference plane when no ceiling exists.
    /// </summary>
    public class PlaceElectricalElementEventHandler : ElectricalHandlerBase<List<ElectricalPlacementInfo>, List<PlacedElementInfo>>
    {
        private const double SearchRadiusFt = 5000 / ElectricalUtils.MmPerFoot;   // 5 m ray length
        private const double WallSearchRadiusFt = 1500 / ElectricalUtils.MmPerFoot; // 1.5 m

        public override string GetName() => "place_electrical_element";

        private View3D _rayView;
        private int _rayViewRun = -1;
        private int _run;

        // Preparing the ray-casting view regenerates the model, so do it once per request.
        private View3D RayView()
        {
            if (_rayViewRun != _run) { _rayView = ElectricalUtils.GetOrCreate3DView(Doc); _rayViewRun = _run; }
            return _rayView;
        }

        protected override AIResult<List<PlacedElementInfo>> Run()
        {
            _run++;
            var results = new List<PlacedElementInfo>();
            if (Input == null || Input.Count == 0)
                return Fail("No elements were given.");

            // One transaction with a sub-transaction per element: committing per element costs many seconds on a large
            // workshared model. A failure that Revit only reports at commit rolls back the whole request.
            using (var t = new Transaction(Doc, "MCP: place electrical elements"))
            {
                t.Start();
                var failures = ElectricalUtils.MakeSilent(t);
                RayView(); // outside the sub-transactions: rolling back the first element must not delete a new view
                foreach (var item in Input)
                {
                    var r = new PlacedElementInfo { Name = item.Name, HostMode = item.HostMode };
                    results.Add(r);
                    using (var sub = new SubTransaction(Doc))
                    {
                        sub.Start();
                        try
                        {
                            PlaceOne(item, r);
                            if (r.ElementId > 0) sub.Commit();
                            else sub.RollBack();
                        }
                        catch (Exception ex)
                        {
                            r.Error = ex.Message;
                            r.ElementId = -1;
                            sub.RollBack();
                        }
                    }
                }
                var status = t.Commit();
                string detail = failures.Messages.Count > 0 ? string.Join(" | ", failures.Messages.Distinct().Take(10)) : "No failure message was reported.";
                foreach (var r in results.Where(x => x.ElementId > 0))
                {
                    // Revit may still roll back or delete the element while committing.
                    if (status != TransactionStatus.Committed || Doc.GetElement(ElectricalUtils.ToElementId(r.ElementId)) == null)
                    {
                        r.Error = $"Revit did not keep the element (commit status {status}). {detail}";
                        r.ElementId = -1;
                    }
                }
                if (status == TransactionStatus.Committed && failures.Messages.Count > 0) Warnings.Add(detail);
            }

            int ok = results.Count(x => x.ElementId > 0);
            string msg = $"Placed {ok} of {results.Count} element(s).";
            if (ok == 0) return Fail(msg + " " + string.Join(" | ", results.Where(x => x.Error != null).Select(x => x.Error)));
            return Ok(results, msg);
        }

        private void PlaceOne(ElectricalPlacementInfo item, PlacedElementInfo r)
        {
            if (item.Location == null) { r.Error = "location is required."; return; }

            var symbol = ElectricalUtils.ResolveSymbol(Doc, item.TypeId, item.Category, item.FamilyName, item.TypeName, out string err);
            if (symbol == null) { r.Error = err; return; }
            r.FamilyName = symbol.FamilyName;
            r.TypeName = symbol.Name;
            if (!symbol.IsActive) symbol.Activate();

            XYZ point = ElectricalUtils.ToXYZ(item.Location);
            Level level = ElectricalUtils.ResolveLevel(Doc, item.LevelName, point.Z);
            if (level == null) { r.Error = "The project has no levels."; return; }

            string mode = (item.HostMode ?? "auto").Trim().ToLowerInvariant();
            var placement = symbol.Family.FamilyPlacementType;
            if (mode == "auto")
                mode = AutoMode(symbol, placement);
            r.HostMode = mode;

            FamilyInstance instance = null;
            switch (mode)
            {
                case "level":
                    instance = PlaceOnLevel(symbol, point, level, placement, r);
                    break;
                case "ceiling":
                    instance = PlaceOnFace(symbol, point, XYZ.BasisZ, CeilingFilter(), item.HostElementId, XYZ.BasisX, r)
                               ?? FallbackToPlane(symbol, point, true, r, "No ceiling or floor found above the point; hosted on a reference plane instead.");
                    break;
                case "floor":
                    instance = PlaceOnFace(symbol, point, -XYZ.BasisZ, new ElementCategoryFilter(BuiltInCategory.OST_Floors), item.HostElementId, XYZ.BasisX, r)
                               ?? FallbackToPlane(symbol, point, false, r, "No floor found below the point; hosted on a reference plane instead.");
                    break;
                case "wall":
                    instance = PlaceOnWall(symbol, point, level, placement, item.HostElementId, r);
                    break;
                case "plane":
                    instance = FallbackToPlane(symbol, point, true, r, null);
                    break;
                default:
                    r.Error = $"Unknown hostMode '{mode}'. Use ceiling, wall, floor, level, plane or auto.";
                    return;
            }

            if (instance == null)
            {
                if (r.Error == null) r.Error = "Revit did not create the instance.";
                return;
            }

            if (Math.Abs(item.Rotation) > 1e-9)
            {
                Doc.Regenerate();
                var loc = instance.Location as LocationPoint;
                XYZ origin = loc?.Point ?? point;
                var axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(Doc, instance.Id, axis, item.Rotation * Math.PI / 180.0);
            }

            if (item.Parameters != null)
            {
                foreach (var kv in item.Parameters)
                {
                    if (!ElectricalUtils.TrySetParameter(instance, kv.Key, kv.Value, out string perr))
                        Warnings.Add($"{item.Name}: {perr}");
                }
            }

            SetScheduleLevel(instance, item, level, r);

            r.ElementId = instance.Id.GetValue();
            r.HostElementId = instance.Host?.Id.GetValue() ?? -1;
        }

        /// <summary>Schedule Level = the requested level (or the resolved one), so elements hosted on linked faces still schedule per floor.</summary>
        private void SetScheduleLevel(FamilyInstance instance, ElectricalPlacementInfo item, Level level, PlacedElementInfo r)
        {
            if (item.ScheduleLevel != null && item.ScheduleLevel.Length == 0) return;
            Level target = level;
            if (!string.IsNullOrWhiteSpace(item.ScheduleLevel))
                target = ElectricalUtils.ResolveLevel(Doc, item.ScheduleLevel, level.Elevation) ?? level;
            var p = instance.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
            if (p == null || p.IsReadOnly) return;
            try
            {
                if (p.AsElementId() != target.Id) p.Set(target.Id);
                r.ScheduleLevel = target.Name;
            }
            catch (Exception ex) { Warnings.Add($"{item.Name}: Schedule Level not set ({ex.Message})."); }
        }

        private static string AutoMode(FamilySymbol symbol, FamilyPlacementType placement)
        {
            var bic = (BuiltInCategory)symbol.Category.Id.GetIntValue();
            switch (placement)
            {
                case FamilyPlacementType.OneLevelBased:
                case FamilyPlacementType.TwoLevelsBased:
                    return "level";
                case FamilyPlacementType.OneLevelBasedHosted:
                    // Wall-hosted families (sockets, switches...). Ceiling-hosted ones use the ceiling.
                    return bic == BuiltInCategory.OST_LightingFixtures ? "ceiling" : "wall";
                case FamilyPlacementType.WorkPlaneBased:
                    return bic == BuiltInCategory.OST_LightingFixtures ? "ceiling" : "wall";
                default:
                    return "level";
            }
        }

        private static ElementFilter CeilingFilter() =>
            new LogicalOrFilter(new ElementCategoryFilter(BuiltInCategory.OST_Ceilings),
                                new ElementCategoryFilter(BuiltInCategory.OST_Floors));

        private FamilyInstance PlaceOnLevel(FamilySymbol symbol, XYZ point, Level level, FamilyPlacementType placement, PlacedElementInfo r)
        {
            if (placement == FamilyPlacementType.WorkPlaneBased)
            {
                // Face-based family asked to be level based: host it on a plane at the point.
                return FallbackToPlane(symbol, point, false, r, null);
            }
            if (placement == FamilyPlacementType.OneLevelBasedHosted)
            {
                r.Error = $"'{symbol.FamilyName}' needs a host. Use hostMode wall or ceiling.";
                return null;
            }

            var instance = Doc.Create.NewFamilyInstance(new XYZ(point.X, point.Y, level.Elevation), symbol, level, StructuralType.NonStructural);
            if (instance == null) return null;
            // Elevation from Level = Z - level elevation
            var elev = instance.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM)
                       ?? instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
            if (elev != null && !elev.IsReadOnly)
                elev.Set(point.Z - level.Elevation);
            return instance;
        }

        private FamilyInstance PlaceOnFace(FamilySymbol symbol, XYZ point, XYZ rayDirection, ElementFilter filter, long hostElementId, XYZ referenceDirection, PlacedElementInfo r)
        {
            var view3D = RayView();
            if (view3D == null) { r.Error = "No 3D view type available for ray casting."; return null; }

            ICollection<BuiltInCategory> linkedCategories = rayDirection.Z > 0
                ? new[] { BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs }
                : new[] { BuiltInCategory.OST_Floors };
            ElementId preferred = hostElementId > 0 ? ElectricalUtils.ToElementId(hostElementId) : null;

            // Start the ray slightly below/above the point so a point exactly on the face still hits.
            XYZ origin = point - rayDirection * (50 / ElectricalUtils.MmPerFoot);
            var hit = ElectricalUtils.FindNearestFace(Doc, view3D, origin, rayDirection, filter, SearchRadiusFt, linkedCategories, preferred);
            if (hit == null)
            {
                Warnings.Add($"{r.Name}: no host face found in direction {(rayDirection.Z > 0 ? "up" : "down")}. Nearby faces: " +
                             ElectricalUtils.DescribeNearbyHits(Doc, view3D, point, SearchRadiusFt));
                return null;
            }

            var reference = hit.GetReference();
            XYZ facePoint = reference.GlobalPoint;
            bool linked = ElectricalUtils.IsLinkedReference(reference);

            if (symbol.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased)
            {
                // Face-based families can sit on faces of linked elements too.
                var inst = Doc.Create.NewFamilyInstance(reference, facePoint, referenceDirection, symbol);
                if (inst != null && linked) Warnings.Add($"{r.Name}: hosted on a face of the linked model ({ElectricalUtils.GetLinkedElement(Doc, reference)?.Category?.Name}).");
                return inst;
            }

            if (linked)
            {
                r.Error = $"'{symbol.FamilyName}' is ceiling/wall-hosted (not face-based), and the host is in a linked model. Use a face-based family, or hostMode plane.";
                return null;
            }
            // Hosted (non face-based) family: use the host element directly.
            var host = Doc.GetElement(reference.ElementId);
            var level = ElectricalUtils.ResolveLevel(Doc, null, facePoint.Z);
            return Doc.Create.NewFamilyInstance(facePoint, symbol, host, level, StructuralType.NonStructural);
        }

        private FamilyInstance PlaceOnWall(FamilySymbol symbol, XYZ point, Level level, FamilyPlacementType placement, long hostElementId, PlacedElementInfo r)
        {
            ElementId preferred = hostElementId > 0 ? ElectricalUtils.ToElementId(hostElementId) : null;

            if (placement == FamilyPlacementType.WorkPlaneBased)
            {
                var view3D = RayView();
                if (view3D == null) { r.Error = "No 3D view type available for ray casting."; return null; }
                var hit = ElectricalUtils.FindNearestWallFace(Doc, view3D, point, WallSearchRadiusFt, preferred);
                if (hit == null)
                {
                    r.Error = "No wall face within 1.5 m of the point (host model and linked models were searched). Nearby faces: " +
                              ElectricalUtils.DescribeNearbyHits(Doc, view3D, point, WallSearchRadiusFt);
                    return null;
                }
                var reference = hit.GetReference();
                XYZ facePoint = reference.GlobalPoint;
                // Keep the requested mounting height; the ray is horizontal so Z is already right.
                // Reference direction must lie in the face: horizontal along the wall (face normal x up), not straight up,
                // otherwise plan symbols of some families end up rotated or far from the wall.
                var toFace = new XYZ(facePoint.X - point.X, facePoint.Y - point.Y, 0);
                XYZ refDir = XYZ.BasisZ;
                if (toFace.GetLength() > 1e-6)
                {
                    var along = toFace.Normalize().CrossProduct(XYZ.BasisZ);
                    if (along.GetLength() > 1e-6) refDir = along.Normalize();
                }
                var inst = Doc.Create.NewFamilyInstance(reference, facePoint, refDir, symbol);
                if (inst != null && ElectricalUtils.IsLinkedReference(reference)) Warnings.Add($"{r.Name}: hosted on a wall of the linked model.");
                return inst;
            }

            // Wall-hosted (OneLevelBasedHosted) or level based: find the nearest wall by its location line.
            Wall wall = null;
            XYZ placePoint = point;
            if (preferred != null) wall = Doc.GetElement(preferred) as Wall;
            if (wall == null)
            {
                // Allow the point to sit up to 300 mm off the wall centerline.
                var near = Doc.GetNearestWallByLocationLine(point, level, 300 / ElectricalUtils.MmPerFoot);
                if (near.HasValue)
                {
                    wall = near.Value.wall;
                    placePoint = new XYZ(near.Value.projectedPoint.X, near.Value.projectedPoint.Y, point.Z);
                }
            }
            if (wall == null)
            {
                r.Error = $"No wall of this model near the point. '{symbol.FamilyName}' is wall-hosted (not face-based), so it cannot use walls from a linked model; pick a face-based family or use hostMode level.";
                return null;
            }

            if (placement == FamilyPlacementType.OneLevelBasedHosted)
            {
                var inst = Doc.Create.NewFamilyInstance(placePoint, symbol, wall, level, StructuralType.NonStructural);
                if (inst != null)
                {
                    var elev = inst.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM)
                               ?? inst.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                    if (elev != null && !elev.IsReadOnly) elev.Set(point.Z - level.Elevation);
                }
                return inst;
            }

            // Level-based family next to a wall: just place it at the projected point.
            var levelInst = Doc.Create.NewFamilyInstance(new XYZ(placePoint.X, placePoint.Y, level.Elevation), symbol, level, StructuralType.NonStructural);
            var p = levelInst?.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
            if (p != null && !p.IsReadOnly) p.Set(point.Z - level.Elevation);
            return levelInst;
        }

        private FamilyInstance FallbackToPlane(FamilySymbol symbol, XYZ point, bool normalDown, PlacedElementInfo r, string warning)
        {
            if (symbol.Family.FamilyPlacementType != FamilyPlacementType.WorkPlaneBased)
            {
                if (r.Error == null) r.Error = $"'{symbol.FamilyName}' is not face-based, so it cannot be hosted on a reference plane. Add a ceiling or use hostMode level.";
                return null;
            }
            string name = $"MCP {(normalDown ? "Ceiling" : "Plane")} {Math.Round(ElectricalUtils.FtToMm(point.Z))}";
            var plane = ElectricalUtils.CreateHorizontalReferencePlane(Doc, point, normalDown, name);
            if (plane == null) { r.Error = "Could not create a reference plane (no plan view)."; return null; }
            var inst = Doc.Create.NewFamilyInstance(plane.GetReference(), point, XYZ.BasisX, symbol);
            if (inst != null && warning != null) Warnings.Add($"{r.Name}: {warning}");
            return inst;
        }
    }
}
