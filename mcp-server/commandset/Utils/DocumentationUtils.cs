using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System.IO;
using RevitMCPCommandSet.Models.Documentation;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>Helpers shared by the documentation (views, sheets, tags, schedules, wires) and family commands.</summary>
    public static class DocumentationUtils
    {
        public static bool Contains(string text, string part) =>
            !string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(part) && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool SameText(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Exact name first, then "contains".</summary>
        public static T PickByName<T>(IEnumerable<T> items, Func<T, string> name, string wanted) where T : class
        {
            if (string.IsNullOrWhiteSpace(wanted)) return null;
            var list = items.ToList();
            return list.FirstOrDefault(i => SameText(name(i), wanted)) ?? list.FirstOrDefault(i => Contains(name(i), wanted));
        }

        /// <summary>The view by id, or the active view when id is not given.</summary>
        public static View ResolveView(Document doc, View activeView, long viewId, out string error)
        {
            error = null;
            if (viewId > 0)
            {
                if (doc.GetElement(ElectricalUtils.ToElementId(viewId)) is View v && !v.IsTemplate) return v;
                error = $"viewId {viewId} is not a view.";
                return null;
            }
            if (activeView == null) error = "No active view.";
            return activeView;
        }

        public static View FindViewByName(Document doc, string name) =>
            PickByName(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate), v => v.Name, name);

        /// <summary>A view name that does not exist yet ("name", "name (2)", ...).</summary>
        public static string UniqueViewName(Document doc, string wanted)
        {
            var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(wanted)) return wanted;
            for (int i = 2; ; i++)
            {
                string candidate = $"{wanted} ({i})";
                if (!names.Contains(candidate)) return candidate;
            }
        }

        public static ViewInfo ToViewInfo(Document doc, View v, Dictionary<long, string> sheetOfView = null)
        {
            var info = new ViewInfo
            {
                Id = v.Id.GetValue(),
                Name = v.Name,
                ViewType = v.ViewType.ToString(),
                Level = v.GenLevel?.Name,
            };
            try { info.Scale = v.Scale; } catch { }
            try { if (v.HasViewDiscipline()) info.Discipline = v.Discipline.ToString(); } catch { }
            if (v.ViewTemplateId != ElementId.InvalidElementId && doc.GetElement(v.ViewTemplateId) is View t) info.Template = t.Name;
            if (sheetOfView != null && sheetOfView.TryGetValue(info.Id, out var sheet)) info.Sheet = sheet;
            return info;
        }

        /// <summary>
        /// A point given in mm for this view: model X/Y for plan views (projected onto the view plane),
        /// view-plane coordinates (right/up from the view origin) for drafting, section and other views.
        /// </summary>
        public static XYZ ViewPoint(View view, double xMm, double yMm)
        {
            double x = ElectricalUtils.MmToFt(xMm), y = ElectricalUtils.MmToFt(yMm);
            XYZ origin = view.Origin;
            if (view is ViewPlan)
            {
                double z = view.GenLevel?.Elevation ?? origin.Z;
                try { if (view.SketchPlane != null) z = view.SketchPlane.GetPlane().Origin.Z; } catch { }
                return new XYZ(x, y, z);
            }
            return origin + view.RightDirection * x + view.UpDirection * y;
        }

        /// <summary>Paper mm in this view, as model feet.</summary>
        public static double PaperToModelFt(View view, double paperMm)
        {
            int scale = 100;
            try { scale = Math.Max(1, view.Scale); } catch { }
            return ElectricalUtils.MmToFt(paperMm * scale);
        }

        /// <summary>A reasonable point to anchor an annotation to an element.</summary>
        public static XYZ AnchorPoint(Element e, View view)
        {
            if (e.Location is LocationPoint lp) return lp.Point;
            if (e.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
            var bb = e.get_BoundingBox(view) ?? e.get_BoundingBox(null);
            return bb == null ? null : (bb.Min + bb.Max) / 2;
        }

        /// <summary>Line style (subcategory of Lines) by name.</summary>
        public static GraphicsStyle FindLineStyle(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var lines = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
            if (lines == null) return null;
            var sub = PickByName(lines.SubCategories.Cast<Category>(), c => c.Name, name);
            return sub?.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        public static List<string> LineStyleNames(Document doc)
        {
            var lines = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
            return lines == null ? new List<string>() : lines.SubCategories.Cast<Category>().Select(c => c.Name).OrderBy(n => n).ToList();
        }

        /// <summary>Folder under Documents\RevitClaude, created when missing.</summary>
        public static string DefaultFolder(string sub)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RevitClaude", sub);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        /// <summary>The tag category that tags elements of this model category (null when none).</summary>
        public static readonly Dictionary<BuiltInCategory, BuiltInCategory> TagCategoryOf = new Dictionary<BuiltInCategory, BuiltInCategory>
        {
            { BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingFixtureTags },
            { BuiltInCategory.OST_LightingDevices, BuiltInCategory.OST_LightingDeviceTags },
            { BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_ElectricalFixtureTags },
            { BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalEquipmentTags },
            { BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_DataDeviceTags },
            { BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_CommunicationDeviceTags },
            { BuiltInCategory.OST_FireAlarmDevices, BuiltInCategory.OST_FireAlarmDeviceTags },
            { BuiltInCategory.OST_SecurityDevices, BuiltInCategory.OST_SecurityDeviceTags },
            { BuiltInCategory.OST_TelephoneDevices, BuiltInCategory.OST_TelephoneDeviceTags },
            { BuiltInCategory.OST_NurseCallDevices, BuiltInCategory.OST_NurseCallDeviceTags },
            { BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayTags },
            { BuiltInCategory.OST_CableTrayFitting, BuiltInCategory.OST_CableTrayFittingTags },
            { BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitTags },
            { BuiltInCategory.OST_ConduitFitting, BuiltInCategory.OST_ConduitFittingTags },
            { BuiltInCategory.OST_Wire, BuiltInCategory.OST_WireTags },
            { BuiltInCategory.OST_Rooms, BuiltInCategory.OST_RoomTags },
            { BuiltInCategory.OST_MEPSpaces, BuiltInCategory.OST_MEPSpaceTags },
            { BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_GenericModelTags },
            { BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_MechanicalEquipmentTags },
        };

        public static ElectricalSystemType ParseSystemType(string text)
        {
            switch ((text ?? "power").Trim().ToLowerInvariant().Replace(" ", "").Replace("_", ""))
            {
                case "power":
                case "powerbalanced": return ElectricalSystemType.PowerBalanced;
                case "powerunbalanced": return ElectricalSystemType.PowerUnBalanced;
                case "data": return ElectricalSystemType.Data;
                case "telephone": return ElectricalSystemType.Telephone;
                case "firealarm": return ElectricalSystemType.FireAlarm;
                case "security": return ElectricalSystemType.Security;
                case "communication": return ElectricalSystemType.Communication;
                case "controls": return ElectricalSystemType.Controls;
                case "nursecall": return ElectricalSystemType.NurseCall;
                default: return ElectricalSystemType.PowerBalanced;
            }
        }
    }
}
