using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Electrical
{
    /// <summary>
    /// Face-based instances hosted on a face of a LINKED model lose their host when the link is reloaded (Archicad/IFC
    /// re-exports change element ids). This re-creates each such instance on a reference plane of the model itself at the
    /// same point and orientation, copies its instance parameters, moves its circuits and tags, and deletes the old one.
    /// </summary>
    public class RehostToWorkPlaneEventHandler : ElectricalHandlerBase<RehostRequest, RehostResult>
    {
        public override string GetName() => "rehost_to_workplane";

        protected override AIResult<RehostResult> Run()
        {
            var req = Input ?? new RehostRequest();
            if (Doc.IsModelInCloud && !req.AllowCloudModel)
                return Fail("The open model is connected to the cloud. Reopen it detached, or pass allowCloudModel once the user has accepted working on it without Sync.");

            var result = new RehostResult { DryRun = req.DryRun };
            var candidates = FindCandidates(req);
            result.Candidates = candidates.Count;
            var batch = candidates.Take(Math.Max(1, req.MaxElements)).ToList();
            result.Processed = batch.Count;
            result.Remaining = candidates.Count - batch.Count;
            if (batch.Count == 0) return Ok(result, "No face-based instances hosted on a linked model were found.");

            // tags per tagged element, collected once
            var tagsOf = new Dictionary<ElementId, List<IndependentTag>>();
            foreach (var tag in new FilteredElementCollector(Doc).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                foreach (var id in tag.GetTaggedLocalElementIds())
                {
                    if (!tagsOf.TryGetValue(id, out var l)) tagsOf[id] = l = new List<IndependentTag>();
                    l.Add(tag);
                }

            var planes = new Dictionary<string, ReferencePlane>();
            using (var group = new TransactionGroup(Doc, "MCP: re-host to work plane"))
            {
                group.Start();
                using (var t = new Transaction(Doc, "MCP: re-host to work plane"))
                {
                    t.Start();
                    var pre = ElectricalUtils.MakeSilent(t);
                    foreach (var fi in batch)
                    {
                        var item = new RehostItem
                        {
                            OldId = fi.Id.GetValue(), Family = fi.Symbol.FamilyName, Type = fi.Symbol.Name,
                            Level = fi.LevelId != ElementId.InvalidElementId ? Doc.GetElement(fi.LevelId)?.Name : null
                        };
                        result.Items.Add(item);
                        try { Rehost(fi, item, planes, tagsOf, result); result.Rehosted++; }
                        catch (Exception ex) { item.Error = ex.Message; result.Failed++; }
                        result.ByFamily[item.Family] = result.ByFamily.TryGetValue(item.Family, out var n) ? n + 1 : 1;
                    }
                    result.PlanesCreated = planes.Count;
                    var status = t.Commit();
                    result.RolledBack = pre.RolledBack || status != TransactionStatus.Committed;
                    Warnings.AddRange(pre.Messages);
                }
                if (req.DryRun || result.RolledBack) group.RollBack(); else group.Assimilate();
            }
            string mode = req.DryRun ? "Dry run, nothing was kept" : result.RolledBack ? "Revit rolled the change back" : "Re-hosted";
            return Ok(result, $"{mode}: {result.Rehosted} of {result.Processed} instances ({result.Candidates} candidates, {result.Remaining} left), " +
                              $"{result.CircuitsMoved} circuits, {result.TagsRecreated} tags, {result.PlanesCreated} planes");
        }

        private List<FamilyInstance> FindCandidates(RehostRequest req)
        {
            IEnumerable<FamilyInstance> all;
            if (req.ElementIds.Count > 0)
                all = req.ElementIds.Select(id => Doc.GetElement(new ElementId(id)) as FamilyInstance).Where(e => e != null);
            else
            {
                var cats = req.Categories.Count == 0 ? ElectricalUtils.DeviceCategories.ToList()
                    : req.Categories.Select(c => ElectricalUtils.TryParseCategory(c, out var b) ? b : BuiltInCategory.INVALID).Where(b => b != BuiltInCategory.INVALID).ToList();
                var filter = new ElementMulticategoryFilter(cats);
                all = new FilteredElementCollector(Doc).OfClass(typeof(FamilyInstance)).WherePasses(filter).Cast<FamilyInstance>();
                if (!string.IsNullOrWhiteSpace(req.LevelName))
                    all = all.Where(fi => fi.LevelId != ElementId.InvalidElementId && Doc.GetElement(fi.LevelId)?.Name == req.LevelName);
            }
            return all.Where(IsOnLinkedFace).OrderBy(fi => fi.Id.GetValue()).ToList();
        }

        private static bool IsOnLinkedFace(FamilyInstance fi)
        {
            if (fi.Symbol.Family.FamilyPlacementType != FamilyPlacementType.WorkPlaneBased) return false;
            if (fi.Host is RevitLinkInstance) return true;
            try { return fi.HostFace != null && fi.HostFace.LinkedElementId != ElementId.InvalidElementId; } catch { return false; }
        }

        private void Rehost(FamilyInstance fi, RehostItem item, Dictionary<string, ReferencePlane> planes,
                            Dictionary<ElementId, List<IndependentTag>> tagsOf, RehostResult result)
        {
            if (!(fi.Location is LocationPoint lp)) throw new InvalidOperationException("no location point");
            XYZ p = lp.Point;
            Transform tr = fi.GetTransform();
            XYZ normal = tr.BasisZ, hand = tr.BasisX;

            // one plane per (normal, offset): a wall face is shared by many devices
            string key = $"{Math.Round(normal.X, 3)},{Math.Round(normal.Y, 3)},{Math.Round(normal.Z, 3)}|{Math.Round(normal.DotProduct(p) * 304.8)}";
            if (!planes.TryGetValue(key, out var plane))
            {
                View view = Doc.ActiveView is ViewPlan ? Doc.ActiveView
                    : new FilteredElementCollector(Doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate);
                if (view == null) throw new InvalidOperationException("no plan view to draw the reference plane in");
                // plane normal = (free - bubble) x cutVec = hand x (normal x hand) = normal
                plane = Doc.Create.NewReferencePlane(p - hand, p + hand, normal.CrossProduct(hand), view);
                try { plane.Name = $"MCP Rehost {planes.Count + 1}"; } catch { }
                planes[key] = plane;
            }

            var ni = Doc.Create.NewFamilyInstance(plane.GetReference(), p, hand, fi.Symbol);
            if (ni == null) throw new InvalidOperationException("Revit refused to place the instance on the plane");
            if (fi.Mirrored != ni.Mirrored) Warnings.Add($"{item.OldId}: the original was mirrored; check the new instance {ni.Id.GetValue()}");

            CopyInstanceParameters(fi, ni);

            var systems = fi.MEPModel?.GetElectricalSystems();
            if (systems != null)
                foreach (var sys in systems.Where(s => s.BaseEquipment == null || s.BaseEquipment.Id != fi.Id))
                {
                    try
                    {
                        var add = new ElementSet(); add.Insert(ni);
                        var remove = new ElementSet(); remove.Insert(fi);
                        sys.AddToCircuit(add);
                        sys.RemoveFromCircuit(remove);
                        item.Circuits++; result.CircuitsMoved++;
                    }
                    catch (Exception ex) { Warnings.Add($"{item.OldId}: circuit {sys.Name}: {ex.Message}"); }
                }

            if (tagsOf.TryGetValue(fi.Id, out var tags))
                foreach (var tag in tags)
                {
                    try
                    {
                        var nt = IndependentTag.Create(Doc, tag.GetTypeId(), tag.OwnerViewId, new Reference(ni), tag.HasLeader, tag.TagOrientation, tag.TagHeadPosition);
                        if (nt != null) { item.Tags++; result.TagsRecreated++; }
                    }
                    catch (Exception ex) { Warnings.Add($"{item.OldId}: tag {tag.Id.GetValue()}: {ex.Message}"); }
                }

            Doc.Delete(fi.Id);
            item.NewId = ni.Id.GetValue();
        }

        private static void CopyInstanceParameters(FamilyInstance from, FamilyInstance to)
        {
            foreach (Parameter p in from.Parameters)
            {
                if (p.IsReadOnly || !p.HasValue) continue;
                var bip = (p.Definition as InternalDefinition)?.BuiltInParameter ?? BuiltInParameter.INVALID;
                // placement parameters belong to the new host; the rest (Mark, Comments, Tpz_*, Workset, Level...) is copied
                if (bip == BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM || bip == BuiltInParameter.INSTANCE_ELEVATION_PARAM) continue;
                var q = to.LookupParameter(p.Definition.Name);
                if (q == null || q.IsReadOnly || q.StorageType != p.StorageType) continue;
                try
                {
                    switch (p.StorageType)
                    {
                        case StorageType.Double: q.Set(p.AsDouble()); break;
                        case StorageType.Integer: q.Set(p.AsInteger()); break;
                        case StorageType.String: q.Set(p.AsString()); break;
                        case StorageType.ElementId: q.Set(p.AsElementId()); break;
                    }
                }
                catch { /* a parameter the new host does not accept is not worth failing the element */ }
            }
        }
    }
}
