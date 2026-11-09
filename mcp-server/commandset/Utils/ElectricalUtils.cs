using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Models.Common;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Helpers shared by the electrical command set. Lengths coming from the
    /// client are millimeters; everything handed to the Revit API is feet.
    /// </summary>
    public static class ElectricalUtils
    {
        public const double MmPerFoot = 304.8;

        public static double MmToFt(double mm) => mm / MmPerFoot;
        public static double FtToMm(double ft) => ft * MmPerFoot;

        public static XYZ ToXYZ(JZPoint p) => new XYZ(MmToFt(p.X), MmToFt(p.Y), MmToFt(p.Z));

        public static ElementId ToElementId(long value)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(value);
#else
            return new ElementId((int)value);
#endif
        }

        /// <summary>Electrical categories we consider "ours".</summary>
        public static readonly BuiltInCategory[] ElectricalCategories =
        {
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalCircuit,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_Wire,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_TelephoneDevices,
            BuiltInCategory.OST_NurseCallDevices,
        };

        /// <summary>Categories whose instances can carry an electrical connector / be circuited.</summary>
        public static readonly BuiltInCategory[] DeviceCategories =
        {
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_TelephoneDevices,
            BuiltInCategory.OST_NurseCallDevices,
        };

        public static bool TryParseCategory(string name, out BuiltInCategory bic)
        {
            bic = BuiltInCategory.INVALID;
            if (string.IsNullOrWhiteSpace(name)) return false;
            string clean = name.Trim().Replace(" ", "");
            if (!clean.StartsWith("OST_", StringComparison.OrdinalIgnoreCase))
                clean = "OST_" + clean;
            return Enum.TryParse(clean, true, out bic) && bic != BuiltInCategory.INVALID;
        }

        /// <summary>Level by name, or the highest level at/below the elevation (ft), or the nearest one.</summary>
        public static Level ResolveLevel(Document doc, string levelName, double elevationFt)
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            if (levels.Count == 0) return null;

            if (!string.IsNullOrWhiteSpace(levelName))
            {
                var byName = levels.FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase))
                          ?? levels.FirstOrDefault(l => l.Name.IndexOf(levelName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (byName != null) return byName;
            }

            // Highest level that is not above the point (with a small tolerance), else the nearest.
            var below = levels.Where(l => l.Elevation <= elevationFt + 1e-6).OrderByDescending(l => l.Elevation).FirstOrDefault();
            return below ?? levels.OrderBy(l => Math.Abs(l.Elevation - elevationFt)).First();
        }

        /// <summary>Find a FamilySymbol by id, or by (category, family name, type name) partial matches.</summary>
        public static FamilySymbol ResolveSymbol(Document doc, long typeId, string category, string familyName, string typeName, out string error)
        {
            error = null;
            if (typeId > 0)
            {
                var el = doc.GetElement(ToElementId(typeId)) as FamilySymbol;
                if (el != null) return el;
                error = $"typeId {typeId} is not a loadable family type (FamilySymbol).";
                return null;
            }

            var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
            if (TryParseCategory(category, out var bic))
                collector = collector.OfCategory(bic);

            var candidates = collector.Cast<FamilySymbol>().AsEnumerable();
            if (!string.IsNullOrWhiteSpace(familyName))
                candidates = candidates.Where(s => s.FamilyName.IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrWhiteSpace(typeName))
                candidates = candidates.Where(s => s.Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0);

            var list = candidates.ToList();
            if (list.Count == 0)
            {
                error = $"No family type found for category='{category}', family~'{familyName}', type~'{typeName}'. Use get_available_family_types or load_family.";
                return null;
            }
            if (list.Count > 1)
            {
                // Prefer an exact type-name match, else the first (deterministic by name).
                var exact = list.FirstOrDefault(s => typeName != null && s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                list = list.OrderBy(s => s.FamilyName).ThenBy(s => s.Name).ToList();
            }
            return list[0];
        }

        /// <summary>Set a parameter from text, converting lengths from mm when the parameter is a length.</summary>
        public static bool TrySetParameter(Element element, string name, string value, out string error)
        {
            error = null;
            Parameter p = element.LookupParameter(name);
            if (p == null)
            {
                error = $"Parameter '{name}' not found on element {element.Id.GetValue()}.";
                return false;
            }
            if (p.IsReadOnly)
            {
                error = $"Parameter '{name}' is read-only.";
                return false;
            }
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        return p.Set(value);
                    case StorageType.Integer:
                        if (bool.TryParse(value, out bool b)) return p.Set(b ? 1 : 0);
                        return p.Set(int.Parse(value));
                    case StorageType.Double:
                        double d = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                        var spec = p.Definition.GetDataType();
                        if (spec == SpecTypeId.Length) d = MmToFt(d);
                        else if (spec == SpecTypeId.Angle) d = d * Math.PI / 180.0;
                        else if (UnitUtils.IsMeasurableSpec(spec))
                        {
                            // Let Revit convert from the project's display unit for this spec.
                            var fo = element.Document.GetUnits().GetFormatOptions(spec);
                            d = UnitUtils.ConvertToInternalUnits(d, fo.GetUnitTypeId());
                        }
                        return p.Set(d);
                    case StorageType.ElementId:
                        return p.Set(ToElementId(long.Parse(value)));
                    default:
                        error = $"Parameter '{name}' has an unsupported storage type.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = $"Could not set '{name}' = '{value}': {ex.Message}";
                return false;
            }
        }

        /// <summary>A non-template 3D view, needed by ReferenceIntersector. Created on demand (inside the caller's transaction).</summary>
        public const string RayCastViewName = "MCP Ray Casting";

        /// <summary>
        /// Dedicated 3D view for ray casting. Model views often hide the architecture (e.g. "3D - Just Electrical"),
        /// so the first 3D view found cannot be trusted; this view has no template and shows walls, floors,
        /// ceilings, roofs and Revit links. Created on demand inside the caller's transaction.
        /// </summary>
        public static View3D GetOrCreate3DView(Document doc)
        {
            var view = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == RayCastViewName);
            if (view == null)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                if (vft == null) return null;
                view = View3D.CreateIsometric(doc, vft.Id);
                try { view.Name = RayCastViewName; } catch { /* name taken by a non-3D view */ }
            }
            PrepareForRayCasting(doc, view);
            return view;
        }

        private static void PrepareForRayCasting(Document doc, View3D view)
        {
            try { if (view.ViewTemplateId != ElementId.InvalidElementId) view.ViewTemplateId = ElementId.InvalidElementId; } catch { }
            try { view.Discipline = ViewDiscipline.Coordination; } catch { }
            try { view.DetailLevel = ViewDetailLevel.Medium; } catch { }
            try { if (view.IsSectionBoxActive) view.IsSectionBoxActive = false; } catch { }
            try { view.AreAnalyticalModelCategoriesHidden = true; } catch { }
            foreach (var bic in new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Ceilings,
                                        BuiltInCategory.OST_Roofs, BuiltInCategory.OST_RvtLinks, BuiltInCategory.OST_StructuralColumns,
                                        BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralFraming })
            {
                try
                {
                    var id = new ElementId(bic);
                    if (view.CanCategoryBeHidden(id) && view.GetCategoryHidden(id)) view.SetCategoryHidden(id, false);
                }
                catch { }
            }
            // Links hidden individually in this view, or hidden worksets, would make linked walls invisible to the ray.
            try
            {
                var hiddenLinks = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance))
                    .Where(l => l.IsHidden(view)).Select(l => l.Id).ToList();
                if (hiddenLinks.Count > 0) view.UnhideElements(hiddenLinks);
            }
            catch { }
            try
            {
                if (doc.IsWorkshared)
                    foreach (var ws in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset))
                        if (view.GetWorksetVisibility(ws.Id) == WorksetVisibility.Hidden) view.SetWorksetVisibility(ws.Id, WorksetVisibility.Visible);
            }
            catch { }
            try { doc.Regenerate(); } catch { }
        }

        /// <summary>
        /// Shoot a ray and return the nearest face reference of an element passing the filter.
        /// Searches the host document first; when nothing is hit and <paramref name="linkedCategories"/>
        /// is given, searches faces of elements of those categories inside linked models too
        /// (electrical models usually get their walls and ceilings from a linked architectural model).
        /// </summary>
        public static ReferenceWithContext FindNearestFace(Document doc, View3D view3D, XYZ origin, XYZ direction,
            ElementFilter filter, double maxDistanceFt, ICollection<BuiltInCategory> linkedCategories = null, ElementId preferredId = null)
        {
            // An explicit host id may belong to this document or to a linked one (ids of linked elements are
            // what users see when they select through a link). Match either.
            bool explicitHost = preferredId != null && preferredId != ElementId.InvalidElementId;
            bool preferredIsLinked = explicitHost && doc.GetElement(preferredId) == null;
            if (explicitHost)
            {
                filter = preferredIsLinked ? null : new ElementIdSetFilter(new[] { preferredId });
                linkedCategories = preferredIsLinked ? new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs } : null;
            }
            // One unfiltered intersector that also looks into links; each hit is then checked by hand.
            // (A filter on a link-aware intersector is applied inconsistently between host and linked hits.)
            var intersector = new ReferenceIntersector(view3D)
            {
                FindReferencesInRevitLinks = linkedCategories != null && linkedCategories.Count > 0,
                TargetType = FindReferenceTarget.Face
            };

            IList<ReferenceWithContext> hits;
            try { hits = intersector.Find(origin, direction); }
            catch { return null; }

            foreach (var hit in hits.OrderBy(h => h.Proximity))
            {
                if (hit.Proximity > maxDistanceFt) break;
                var reference = hit.GetReference();
                if (reference == null) continue;

                if (IsLinkedReference(reference))
                {
                    var linkedElement = GetLinkedElement(doc, reference);
                    if (linkedElement?.Category == null) continue;
                    if (preferredIsLinked) { if (linkedElement.Id == preferredId) return hit; continue; }
                    var bic = (BuiltInCategory)linkedElement.Category.Id.GetIntValue();
                    if (linkedCategories != null && linkedCategories.Contains(bic)) return hit;
                    continue;
                }

                var element = doc.GetElement(reference.ElementId);
                if (element == null || element is RevitLinkInstance) continue;
                if (filter == null || filter.PassesFilter(element)) return hit;
            }
            return null;
        }

        /// <summary>Human-readable list of what the rays hit, for error messages.</summary>
        public static string DescribeNearbyHits(Document doc, View3D view3D, XYZ origin, double maxDistanceFt)
        {
            var intersector = new ReferenceIntersector(view3D) { FindReferencesInRevitLinks = true, TargetType = FindReferenceTarget.Face };
            var seen = new List<string>();
            var dirs = new List<XYZ> { XYZ.BasisZ, -XYZ.BasisZ };
            for (int i = 0; i < 8; i++) dirs.Add(new XYZ(Math.Cos(i * Math.PI / 4), Math.Sin(i * Math.PI / 4), 0));
            foreach (var dir in dirs)
            {
                IList<ReferenceWithContext> hits;
                try { hits = intersector.Find(origin, dir); } catch { continue; }
                foreach (var h in hits.OrderBy(h => h.Proximity).Take(2))
                {
                    if (h.Proximity > maxDistanceFt) break;
                    var r = h.GetReference();
                    var el = IsLinkedReference(r) ? GetLinkedElement(doc, r) : doc.GetElement(r.ElementId);
                    string tag = $"{(IsLinkedReference(r) ? "link:" : "")}{el?.Category?.Name ?? el?.GetType().Name ?? "?"}@{Math.Round(FtToMm(h.Proximity))}mm";
                    if (!seen.Contains(tag)) seen.Add(tag);
                }
            }
            int linkCount = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount();
            return seen.Count == 0 ? $"nothing hit (links in model: {linkCount})" : string.Join(", ", seen.Take(8)) + $" (links: {linkCount})";
        }

        /// <summary>The element inside a linked model that a reference points at, or null.</summary>
        public static Element GetLinkedElement(Document doc, Reference reference)
        {
            if (reference == null || reference.LinkedElementId == ElementId.InvalidElementId) return null;
            var link = doc.GetElement(reference.ElementId) as RevitLinkInstance;
            var linkDoc = link?.GetLinkDocument();
            return linkDoc?.GetElement(reference.LinkedElementId);
        }

        public static bool IsLinkedReference(Reference reference) =>
            reference != null && reference.LinkedElementId != ElementId.InvalidElementId;

        /// <summary>Nearest wall face around a point, looking horizontally in 8 directions, in the host model and then in links.</summary>
        public static ReferenceWithContext FindNearestWallFace(Document doc, View3D view3D, XYZ origin, double maxDistanceFt, ElementId preferredWallId = null)
        {
            ElementFilter filter = new ElementClassFilter(typeof(Wall));
            var linked = new[] { BuiltInCategory.OST_Walls };

            ReferenceWithContext best = null;
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4.0;
                var dir = new XYZ(Math.Cos(a), Math.Sin(a), 0);
                var hit = FindNearestFace(doc, view3D, origin, dir, filter, maxDistanceFt, linked, preferredWallId);
                if (hit != null && (best == null || hit.Proximity < best.Proximity))
                    best = hit;
            }
            return best;
        }

        /// <summary>A reference-plane name not yet used in the document ("base", "base (2)", ...).</summary>
        public static string UniqueReferencePlaneName(Document doc, string baseName)
        {
            var used = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>().Select(rp => rp.Name ?? ""), StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(baseName)) return baseName;
            for (int i = 2; ; i++)
            {
                var candidate = $"{baseName} ({i})";
                if (!used.Contains(candidate)) return candidate;
            }
        }

        /// <summary>Horizontal reference plane at the given elevation. normalDown=true makes the plane face downward (ceiling-like).</summary>
        public static ReferencePlane CreateHorizontalReferencePlane(Document doc, XYZ at, bool normalDown, string name)
        {
            View view = doc.ActiveView is ViewPlan ? doc.ActiveView
                : new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate);
            if (view == null) return null;

            XYZ bubbleEnd = new XYZ(at.X - 1, at.Y, at.Z);
            XYZ freeEnd = new XYZ(at.X + 1, at.Y, at.Z);
            // normal = (free - bubble) x cutVec ; X x (-Y) = -Z (down), X x Y = +Z (up)
            XYZ cutVec = normalDown ? -XYZ.BasisY : XYZ.BasisY;
            var plane = doc.Create.NewReferencePlane(bubbleEnd, freeEnd, cutVec, view);
            try { plane.Name = UniqueReferencePlaneName(doc, name); } catch { /* unnamed plane still works */ }
            return plane;
        }

        public static string DescribeSymbol(FamilySymbol s) => $"{s.FamilyName}: {s.Name} (id {s.Id.GetValue()})";

        public static FamilyTypeInfo ToTypeInfo(ElementType t)
        {
            string family = t is FamilySymbol fs ? fs.FamilyName
                : t.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString() ?? t.GetType().Name;
            return new FamilyTypeInfo
            {
                FamilyTypeId = t.Id.GetValue(),
                UniqueId = t.UniqueId,
                FamilyName = family,
                TypeName = t.Name,
                Category = t.Category?.Name
            };
        }

        public static double ToVoltAmperes(double internalValue) =>
            UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.VoltAmperes);

        public static double ToVolts(double internalValue) =>
            UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Volts);

        /// <summary>
        /// Configure a transaction so Revit never shows a failure dialog: warnings are
        /// deleted, errors are auto-resolved when possible, otherwise rolled back.
        /// A modal dialog inside Revit would block every later MCP request.
        /// </summary>
        public static SilentFailuresPreprocessor MakeSilent(Transaction t)
        {
            var pre = new SilentFailuresPreprocessor();
            var fho = t.GetFailureHandlingOptions();
            fho.SetFailuresPreprocessor(pre);
            fho.SetClearAfterRollback(true);
            fho.SetForcedModalHandling(false);
            t.SetFailureHandlingOptions(fho);
            return pre;
        }

        /// <summary>
        /// Warnings are deleted. Errors are resolved only when the resolution does not delete
        /// elements; otherwise the transaction is rolled back. Everything seen is recorded in
        /// <see cref="Messages"/> so the caller can report it instead of a dialog.
        /// </summary>
        public class SilentFailuresPreprocessor : IFailuresPreprocessor
        {
            public List<string> Messages { get; } = new List<string>();
            public bool RolledBack { get; private set; }

            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                bool rollback = false;
                foreach (var f in accessor.GetFailureMessages())
                {
                    string text = f.GetDescriptionText();
                    var severity = f.GetSeverity();
                    if (severity == FailureSeverity.Warning)
                    {
                        Messages.Add($"warning: {text}");
                        accessor.DeleteWarning(f);
                        continue;
                    }
                    bool deletes = false;
                    try { deletes = f.GetCurrentResolutionType() == FailureResolutionType.DeleteElements; } catch { }
                    if (f.HasResolutions() && !deletes)
                    {
                        Messages.Add($"error resolved ({f.GetCurrentResolutionType()}): {text}");
                        accessor.ResolveFailure(f);
                    }
                    else
                    {
                        Messages.Add($"error, rolled back: {text}");
                        rollback = true;
                    }
                }
                if (rollback) RolledBack = true;
                return rollback ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }

        public static Connector FindConnectorAt(ConnectorManager manager, XYZ point, double toleranceFt = 0.01)
        {
            if (manager == null) return null;
            foreach (Connector c in manager.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End) continue;
                if (c.Origin.DistanceTo(point) <= toleranceFt) return c;
            }
            return null;
        }
    }
}
