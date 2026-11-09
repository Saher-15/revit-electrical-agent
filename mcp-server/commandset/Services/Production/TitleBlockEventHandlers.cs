using System.IO;
using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Production;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Production
{
    /// <summary>Lists the fields a title block shows: Project Information, sheet and title block parameters, with their values.</summary>
    public class GetTitleBlockFieldsEventHandler : ElectricalHandlerBase<SheetSelection, TitleBlockFieldsResult>
    {
        public override string GetName() => "get_title_block_fields";

        private static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Family", "Family and Type", "Type", "Type Id", "Category", "Design Option", "Phase Created", "Phase Demolished", "Image", "Type Image" };

        protected override AIResult<TitleBlockFieldsResult> Run()
        {
            var sel = Input ?? new SheetSelection();
            var notFound = new List<string>();
            var sheets = ProductionUtils.SelectSheets(Doc, sel.SheetIds, sel.SheetNumbers, notFound);
            notFound.ForEach(n => Warnings.Add("Not found: " + n));
            var result = new TitleBlockFieldsResult { SheetCount = sheets.Count };
            var fields = new Dictionary<string, TitleBlockField>();

            void Add(Element e, string source)
            {
                if (e == null) return;
                foreach (Parameter p in e.Parameters)
                {
                    string name = p.Definition?.Name;
                    if (string.IsNullOrEmpty(name) || Skip.Contains(name)) continue;
                    if (p.StorageType == StorageType.None) continue;
                    string key = source + "|" + name;
                    if (!fields.TryGetValue(key, out var f))
                        fields[key] = f = new TitleBlockField { Name = name, Source = source, Type = p.StorageType.ToString(), ReadOnly = p.IsReadOnly };
                    string v = ProductionUtils.ParamText(p);
                    if (string.IsNullOrWhiteSpace(v)) f.EmptyOnSheets++;
                    else if (f.Values.Count < 5 && !f.Values.Contains(v)) f.Values.Add(v);
                }
            }

            Add(Doc.ProjectInformation, "project");
            var tbNames = new HashSet<string>();
            foreach (var s in sheets)
            {
                Add(s, "sheet");
                var tb = ProductionUtils.TitleBlock(Doc, s);
                if (tb == null) { Warnings.Add($"Sheet {s.SheetNumber} has no title block."); continue; }
                tbNames.Add($"{tb.Symbol.FamilyName}: {tb.Symbol.Name}");
                Add(tb, "titleblock");
            }
            foreach (var tbType in new FilteredElementCollector(Doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType()
                         .Cast<FamilySymbol>().Where(t => tbNames.Contains($"{t.FamilyName}: {t.Name}")))
                Add(tbType, "titleblock-type");

            result.TitleBlocks = tbNames.OrderBy(n => n).ToList();
            result.Fields = fields.Values.OrderBy(f => f.Source).ThenBy(f => f.Name).ToList();
            // Project Information is shared, so "empty on sheets" means "empty" there.
            foreach (var f in result.Fields.Where(f => f.Source == "project")) f.EmptyOnSheets = f.EmptyOnSheets > 0 ? sheets.Count : 0;
            return Ok(result, $"{sheets.Count} sheet(s), {result.TitleBlocks.Count} title block type(s), {result.Fields.Count} field(s).");
        }
    }

    /// <summary>Fills title block fields on many sheets at once and reports fields that stay empty.</summary>
    public class FillTitleBlocksEventHandler : ElectricalHandlerBase<FillTitleBlocksRequest, FillTitleBlocksResult>
    {
        public override string GetName() => "fill_title_blocks";

        protected override AIResult<FillTitleBlocksResult> Run()
        {
            var req = Input ?? new FillTitleBlocksRequest();
            var result = new FillTitleBlocksResult();
            var sheets = ProductionUtils.SelectSheets(Doc, req.SheetIds, req.SheetNumbers, result.NotFound);
            if (sheets.Count == 0) return Fail("No sheets selected (or the project has no sheets).");

            FamilySymbol newTb = null;
            if (!string.IsNullOrWhiteSpace(req.TitleBlock))
            {
                var types = new FilteredElementCollector(Doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Cast<FamilySymbol>().ToList();
                newTb = DocumentationUtils.PickByName(types, t => $"{t.FamilyName}: {t.Name}", req.TitleBlock);
                if (newTb == null) return Fail($"Title block '{req.TitleBlock}' not found. Loaded: {string.Join(", ", types.Select(t => $"{t.FamilyName}: {t.Name}"))}");
            }

            var unmatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var t = new Transaction(Doc, "MCP: fill title blocks"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);

                // Project Information is one element shared by all sheets.
                var global = new Dictionary<string, string>(req.Values ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var kv in global.ToList())
                {
                    var p = Doc.ProjectInformation.LookupParameter(kv.Key);
                    if (p == null || p.IsReadOnly) continue;
                    if (req.OnlyEmpty && !string.IsNullOrWhiteSpace(ProductionUtils.ParamText(p))) { global.Remove(kv.Key); continue; }
                    if (ElectricalUtils.TrySetParameter(Doc.ProjectInformation, kv.Key, kv.Value, out string err)) result.ProjectInfoSet.Add(kv.Key);
                    else Warnings.Add(err);
                    global.Remove(kv.Key);
                }

                foreach (var s in sheets)
                {
                    var r = new FilledSheet { Id = s.Id.GetValue(), Number = s.SheetNumber, Name = s.Name };
                    result.Sheets.Add(r);
                    try
                    {
                        var tb = ProductionUtils.TitleBlock(Doc, s);
                        if (newTb != null && tb != null && tb.Symbol.Id != newTb.Id)
                        {
                            if (!newTb.IsActive) newTb.Activate();
                            tb.ChangeTypeId(newTb.Id);
                            r.Set.Add("title block -> " + $"{newTb.FamilyName}: {newTb.Name}");
                        }
                        var values = new Dictionary<string, string>(global, StringComparer.OrdinalIgnoreCase);
                        if (req.PerSheet != null && req.PerSheet.TryGetValue(s.SheetNumber, out var mine))
                            foreach (var kv in mine) values[kv.Key] = kv.Value;

                        foreach (var kv in values)
                        {
                            Element target = null;
                            if (kv.Key.Equals("Sheet Number", StringComparison.OrdinalIgnoreCase)) { s.SheetNumber = kv.Value; r.Set.Add(kv.Key); continue; }
                            if (kv.Key.Equals("Sheet Name", StringComparison.OrdinalIgnoreCase)) { s.Name = kv.Value; r.Set.Add(kv.Key); continue; }
                            var ps = s.LookupParameter(kv.Key);
                            if (ps != null && !ps.IsReadOnly) target = s;
                            else if (tb != null && tb.LookupParameter(kv.Key) is Parameter pt && !pt.IsReadOnly) target = tb;
                            else if (Doc.ProjectInformation.LookupParameter(kv.Key) is Parameter pp && !pp.IsReadOnly) target = Doc.ProjectInformation;
                            if (target == null)
                            {
                                bool exists = ps != null || tb?.LookupParameter(kv.Key) != null;
                                if (exists) r.Errors.Add($"'{kv.Key}' is read-only (it may be a calculated or type parameter).");
                                else unmatched.Add(kv.Key);
                                continue;
                            }
                            if (req.OnlyEmpty && !string.IsNullOrWhiteSpace(ProductionUtils.ParamText(target.LookupParameter(kv.Key)))) continue;
                            if (ElectricalUtils.TrySetParameter(target, kv.Key, kv.Value, out string err)) r.Set.Add(kv.Key);
                            else r.Errors.Add(err);
                        }
                    }
                    catch (Exception ex) { r.Errors.Add(ex.Message); }
                }
                t.Commit();
                Warnings.AddRange(pre.Messages);
                if (pre.RolledBack) return Fail("Revit rolled the changes back.");
            }

            // Check required fields after the commit, on whichever element holds them.
            foreach (var r in result.Sheets)
            {
                var s = Doc.GetElement(ElectricalUtils.ToElementId(r.Id)) as ViewSheet;
                var tb = ProductionUtils.TitleBlock(Doc, s);
                foreach (var f in req.RequiredFields ?? new List<string>())
                {
                    var p = s.LookupParameter(f) ?? tb?.LookupParameter(f) ?? Doc.ProjectInformation.LookupParameter(f);
                    if (p == null || string.IsNullOrWhiteSpace(ProductionUtils.ParamText(p))) r.Missing.Add(f);
                }
            }
            result.NotFound.AddRange(unmatched.Select(u => $"parameter '{u}' (not on Project Information, the sheets or the title block)"));
            int missing = result.Sheets.Count(s => s.Missing.Count > 0);
            return Ok(result, $"{result.Sheets.Count} sheet(s) filled" + (missing > 0 ? $"; {missing} sheet(s) still have empty required fields." : ".") );
        }
    }

    /// <summary>Places the engineer's stamp image on sheets. The MCP tool only calls this after the engineer approved.</summary>
    public class PlaceStampEventHandler : ElectricalHandlerBase<PlaceStampRequest, PlaceStampResult>
    {
        public override string GetName() => "place_stamp";
        public const string StampMark = "Claude stamp";

        protected override AIResult<PlaceStampResult> Run()
        {
            var req = Input ?? new PlaceStampRequest();
            if (string.IsNullOrWhiteSpace(req.ImagePath) || !File.Exists(req.ImagePath)) return Fail($"Stamp image not found: {req.ImagePath}");
            var notFound = new List<string>();
            var sheets = ProductionUtils.SelectSheets(Doc, req.SheetIds, req.SheetNumbers, notFound);
            if ((req.SheetIds?.Count ?? 0) + (req.SheetNumbers?.Count ?? 0) == 0)
                return Fail("Name the sheets to stamp (sheetIds or sheetNumbers); stamping all sheets is not allowed by default.");
            notFound.ForEach(n => Warnings.Add("Not found: " + n));
            var result = new PlaceStampResult();

            using (var t = new Transaction(Doc, "MCP: place stamp"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);
                ImageType type = FindOrCreateImageType(req.ImagePath);
                result.ImageTypeId = type.Id.GetValue();

                foreach (var s in sheets)
                {
                    var existing = new FilteredElementCollector(Doc, s.Id).OfClass(typeof(ImageInstance)).Cast<ImageInstance>()
                        .Where(i => (i.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "").StartsWith(StampMark)).ToList();
                    if (existing.Count > 0 && !req.Replace) { result.Skipped.Add($"{s.SheetNumber}: already stamped (pass replace=true to place it again)"); continue; }
                    foreach (var e in existing) Doc.Delete(e.Id);

                    var tb = ProductionUtils.TitleBlockOutline(Doc, s);
                    double minX = tb?.MinimumPoint.X ?? 0, minY = tb?.MinimumPoint.Y ?? 0, maxX = tb?.MaximumPoint.X ?? ElectricalUtils.MmToFt(420);
                    double dx = ElectricalUtils.MmToFt(req.XMm ?? 60), dy = ElectricalUtils.MmToFt(req.YMm ?? 60);
                    var at = new XYZ(req.FromRight ? maxX - dx : minX + dx, minY + dy, 0);

                    var img = ImageInstance.Create(Doc, s, type.Id, new ImagePlacementOptions(at, BoxPlacement.Center));
                    double w = ElectricalUtils.MmToFt(req.WidthMm > 0 ? req.WidthMm : 40);
                    if (img.Width > 0)
                    {
                        double ratio = img.Height / img.Width;
                        img.Width = w;
                        img.Height = w * ratio;
                        img.SetLocation(at, BoxPlacement.Center);
                    }
                    img.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"{StampMark} | approved by {req.ApprovedBy} | {DateTime.Now:yyyy-MM-dd HH:mm}");
                    result.Placed.Add(new StampedSheet { Sheet = s.SheetNumber, ImageId = img.Id.GetValue(), XMm = Math.Round(ElectricalUtils.FtToMm(at.X), 1), YMm = Math.Round(ElectricalUtils.FtToMm(at.Y), 1) });
                }
                t.Commit();
                Warnings.AddRange(pre.Messages);
                if (pre.RolledBack) return Fail("Revit rolled the stamps back.");
            }
            return Ok(result, $"Stamp placed on {result.Placed.Count} sheet(s), skipped {result.Skipped.Count}.");
        }

        private ImageType FindOrCreateImageType(string path)
        {
            string full = Path.GetFullPath(path);
            foreach (var it in new FilteredElementCollector(Doc).OfClass(typeof(ImageType)).Cast<ImageType>())
            {
                try { if (string.Equals(Path.GetFullPath(it.Path ?? ""), full, StringComparison.OrdinalIgnoreCase)) return it; } catch { }
            }
            return ImageType.Create(Doc, new ImageTypeOptions(full, false, ImageTypeSource.Import));
        }
    }
}
