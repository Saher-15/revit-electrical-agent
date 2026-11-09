using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>Tags elements in a view by category or id. Rooms of linked (architectural) models can be tagged too.</summary>
    public class TagElementsEventHandler : ElectricalHandlerBase<TagRequest, TagResult>
    {
        public override string GetName() => "tag_elements";

        protected override AIResult<TagResult> Run()
        {
            var req = Input ?? new TagRequest();
            View view = DocumentationUtils.ResolveView(Doc, UiApp.ActiveUIDocument.ActiveView, req.ViewId, out string viewError);
            if (view == null) return Fail(viewError);
            if (view is ViewSheet || view is ViewSchedule || view.ViewType == ViewType.ThreeD && !((View3D)view).IsLocked)
                return Fail($"'{view.Name}' ({view.ViewType}) cannot hold tags. Use a plan, section or locked 3D view.");

            var result = new TagResult { ViewId = view.Id.GetValue() };
            var cats = new List<BuiltInCategory>();
            foreach (var c in req.Categories ?? new List<string>())
            {
                if (ElectricalUtils.TryParseCategory(c, out var bic)) cats.Add(bic);
                else result.Errors.Add($"Unknown category '{c}'.");
            }
            if (cats.Count == 0 && (req.ElementIds == null || req.ElementIds.Count == 0))
                return Fail("Give categories (e.g. OST_LightingFixtures) or elementIds.");

            XYZ offset = new XYZ(DocumentationUtils.PaperToModelFt(view, req.OffsetX), DocumentationUtils.PaperToModelFt(view, req.OffsetY), 0);
            if (!(view is ViewPlan)) offset = view.RightDirection * offset.X + view.UpDirection * offset.Y;
            var orientation = req.Vertical ? TagOrientation.Vertical : TagOrientation.Horizontal;

            var tagged = new HashSet<long>();
            if (req.SkipTagged)
            {
                foreach (var tag in new FilteredElementCollector(Doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                    foreach (var id in tag.GetTaggedLocalElementIds()) tagged.Add(id.GetValue());
                foreach (var rt in new FilteredElementCollector(Doc, view.Id).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType().OfType<RoomTag>())
                {
                    try
                    {
                        var lid = rt.TaggedRoomId;
                        if (lid == null) continue;
                        if (lid.LinkInstanceId == ElementId.InvalidElementId) tagged.Add(lid.HostElementId.GetValue());
                        else tagged.Add(lid.LinkedElementId.GetValue() ^ (lid.LinkInstanceId.GetValue() << 32));
                    }
                    catch { }
                }
            }

            using (var t = new Transaction(Doc, "MCP: tag elements"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);
                FamilySymbol tagType = ResolveTagType(req, result);
                var missingTagFamily = new HashSet<string>();

                var targets = new List<Element>();
                foreach (var bic in cats.Where(c => c != BuiltInCategory.OST_Rooms))
                    targets.AddRange(new FilteredElementCollector(Doc, view.Id).OfCategory(bic).WhereElementIsNotElementType().ToElements());
                foreach (long id in req.ElementIds ?? new List<long>())
                {
                    var e = Doc.GetElement(ElectricalUtils.ToElementId(id));
                    if (e != null) targets.Add(e); else result.Errors.Add($"Element {id} not found.");
                }

                foreach (var e in targets.GroupBy(e => e.Id.GetValue()).Select(g => g.First()))
                {
                    string catName = e.Category?.Name ?? "?";
                    if (tagged.Contains(e.Id.GetValue())) { result.SkippedAlreadyTagged++; continue; }
                    if (e is Room room) { TagRoom(room, null, view, offset, tagType, result, catName); continue; }
                    XYZ anchor = DocumentationUtils.AnchorPoint(e, view);
                    if (anchor == null) { result.Errors.Add($"{e.Id.GetValue()}: no location."); continue; }
                    try
                    {
                        var reference = new Reference(e);
                        IndependentTag tag = tagType != null && tagType.Category != null && IsTagFor(tagType, e)
                            ? IndependentTag.Create(Doc, tagType.Id, view.Id, reference, req.AddLeader, orientation, anchor + offset)
                            : IndependentTag.Create(Doc, view.Id, reference, req.AddLeader, TagMode.TM_ADDBY_CATEGORY, orientation, anchor + offset);
                        Count(result, tag.Id, catName);
                    }
                    catch (Exception ex)
                    {
                        if (missingTagFamily.Add(catName))
                            result.Errors.Add($"{catName}: {ex.Message} Load a tag family for this category (load_family nameFilter 'Tag').");
                    }
                }

                if (cats.Contains(BuiltInCategory.OST_Rooms))
                {
                    foreach (var room in new FilteredElementCollector(Doc, view.Id).OfCategory(BuiltInCategory.OST_Rooms).OfType<Room>())
                    {
                        if (room.Area <= 0) continue;
                        if (tagged.Contains(room.Id.GetValue())) { result.SkippedAlreadyTagged++; continue; }
                        TagRoom(room, null, view, offset, tagType, result, "Rooms");
                    }
                    if (req.IncludeLinkedRooms && view is ViewPlan plan && plan.GenLevel != null)
                    {
                        foreach (var link in new FilteredElementCollector(Doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                        {
                            var linkDoc = link.GetLinkDocument();
                            if (linkDoc == null) continue;
                            if (link.IsHidden(view)) continue;
                            var tf = link.GetTotalTransform();
                            foreach (var room in new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_Rooms).OfType<Room>())
                            {
                                if (room.Area <= 0 || !(room.Location is LocationPoint lp)) continue;
                                double z = tf.OfPoint(lp.Point).Z;
                                // Rooms of the level shown in this plan only.
                                if (Math.Abs(z - plan.GenLevel.Elevation) > ElectricalUtils.MmToFt(1500)) continue;
                                long key = room.Id.GetValue() ^ (link.Id.GetValue() << 32);
                                if (tagged.Contains(key)) { result.SkippedAlreadyTagged++; continue; }
                                TagRoom(room, link, view, offset, tagType, result, "Rooms (link)");
                            }
                        }
                    }
                }

                t.Commit();
                Warnings.AddRange(pre.Messages.Take(10));
                if (pre.RolledBack) return Fail("Revit rolled the tags back.");
            }

            string msg = $"Created {result.Created} tag(s) in '{view.Name}'" + (result.SkippedAlreadyTagged > 0 ? $", {result.SkippedAlreadyTagged} already tagged" : "") + ".";
            if (result.Created == 0 && result.Errors.Count > 0) return Fail(msg + " " + string.Join(" ", result.Errors));
            return Ok(result, msg);
        }

        private void TagRoom(Room room, RevitLinkInstance link, View view, XYZ offset, FamilySymbol tagType, TagResult result, string catName)
        {
            try
            {
                if (!(room.Location is LocationPoint lp)) return;
                XYZ p = link == null ? lp.Point : link.GetTotalTransform().OfPoint(lp.Point);
                var linkId = link == null ? new LinkElementId(room.Id) : new LinkElementId(link.Id, room.Id);
                var tag = Doc.Create.NewRoomTag(linkId, new UV(p.X + offset.X, p.Y + offset.Y), view.Id);
                if (tag == null) { result.Errors.Add($"Room '{room.Name}': Revit did not create a tag."); return; }
                if (tagType != null && tagType.Category?.Id.GetValue() == (long)BuiltInCategory.OST_RoomTags) tag.ChangeTypeId(tagType.Id);
                Count(result, tag.Id, catName);
            }
            catch (Exception ex) { result.Errors.Add($"Room '{room.Name}': {ex.Message}"); }
        }

        private static bool IsTagFor(FamilySymbol tagType, Element e)
        {
            if (e.Category == null) return false;
            var bic = (BuiltInCategory)e.Category.Id.GetValue();
            if (tagType.Category.Id.GetValue() == (long)BuiltInCategory.OST_MultiCategoryTags) return true;
            return DocumentationUtils.TagCategoryOf.TryGetValue(bic, out var tagCat) && tagType.Category.Id.GetValue() == (long)tagCat;
        }

        private FamilySymbol ResolveTagType(TagRequest req, TagResult result)
        {
            if (req.TagTypeId > 0)
            {
                var s = Doc.GetElement(ElectricalUtils.ToElementId(req.TagTypeId)) as FamilySymbol;
                if (s == null) result.Errors.Add($"tagTypeId {req.TagTypeId} is not a tag type; using the category default.");
                return s;
            }
            if (string.IsNullOrWhiteSpace(req.TagTypeName)) return null;
            var tagCats = new HashSet<long>(DocumentationUtils.TagCategoryOf.Values.Select(c => (long)c)) { (long)BuiltInCategory.OST_MultiCategoryTags };
            var candidates = new FilteredElementCollector(Doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(s => s.Category != null && tagCats.Contains(s.Category.Id.GetValue()));
            var found = DocumentationUtils.PickByName(candidates, s => $"{s.FamilyName}: {s.Name}", req.TagTypeName);
            if (found == null) result.Errors.Add($"Tag type '{req.TagTypeName}' not found; using the category default.");
            else if (!found.IsActive) found.Activate();
            return found;
        }

        private static void Count(TagResult result, ElementId tagId, string catName)
        {
            result.Created++;
            result.TagIds.Add(tagId.GetValue());
            result.PerCategory[catName] = result.PerCategory.TryGetValue(catName, out int n) ? n + 1 : 1;
        }
    }
}
