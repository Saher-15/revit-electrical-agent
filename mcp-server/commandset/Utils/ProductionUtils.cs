using Autodesk.Revit.DB;

namespace RevitMCPCommandSet.Utils
{
    public static class ProductionUtils
    {
        public static ViewSheet FindSheet(Document doc, long sheetId, string sheetNumber, out string error)
        {
            error = null;
            if (sheetId > 0)
            {
                if (doc.GetElement(ElectricalUtils.ToElementId(sheetId)) is ViewSheet s) return s;
                error = $"Element {sheetId} is not a sheet.";
                return null;
            }
            if (!string.IsNullOrWhiteSpace(sheetNumber))
            {
                var sheet = AllSheets(doc).FirstOrDefault(x => DocumentationUtils.SameText(x.SheetNumber, sheetNumber));
                if (sheet != null) return sheet;
                error = $"No sheet numbered '{sheetNumber}'.";
                return null;
            }
            error = "Give sheetId or sheetNumber.";
            return null;
        }

        public static List<ViewSheet> AllSheets(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && !s.IsTemplate).OrderBy(s => s.SheetNumber).ToList();

        /// <summary>Selected sheets: by ids and numbers, or all sheets when both are empty.</summary>
        public static List<ViewSheet> SelectSheets(Document doc, IList<long> ids, IList<string> numbers, List<string> notFound)
        {
            var all = AllSheets(doc);
            if ((ids == null || ids.Count == 0) && (numbers == null || numbers.Count == 0)) return all;
            var picked = new List<ViewSheet>();
            foreach (var id in ids ?? new List<long>())
            {
                var s = all.FirstOrDefault(x => x.Id.GetValue() == id);
                if (s != null) picked.Add(s); else notFound?.Add($"sheet id {id}");
            }
            foreach (var n in numbers ?? new List<string>())
            {
                var s = all.FirstOrDefault(x => DocumentationUtils.SameText(x.SheetNumber, n));
                if (s != null) picked.Add(s); else notFound?.Add($"sheet {n}");
            }
            return picked.Distinct().ToList();
        }

        public static FamilyInstance TitleBlock(Document doc, ViewSheet sheet) =>
            new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().FirstOrDefault();

        /// <summary>Title block extents on the sheet (feet), or null when the sheet has none.</summary>
        public static Outline TitleBlockOutline(Document doc, ViewSheet sheet)
        {
            var tb = TitleBlock(doc, sheet);
            var bb = tb?.get_BoundingBox(sheet);
            return bb == null ? null : new Outline(bb.Min, bb.Max);
        }

        public static string ParamText(Parameter p)
        {
            if (p == null || !p.HasValue) return "";
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString() ?? "";
                case StorageType.ElementId: return p.AsValueString() ?? "";
                default: return p.AsValueString() ?? "";
            }
        }
    }
}
