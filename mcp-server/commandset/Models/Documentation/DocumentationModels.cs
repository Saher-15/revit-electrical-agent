using Newtonsoft.Json;
using RevitMCPCommandSet.Models.Common;

namespace RevitMCPCommandSet.Models.Documentation
{
    // ------------------------------------------------------------------ get_documentation_info

    public class DocumentationInfoRequest
    {
        /// <summary>Only views whose level name contains this text.</summary>
        [JsonProperty("levelName")] public string LevelName { get; set; }
    }

    public class NamedId
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)] public string Kind { get; set; }
    }

    public class DocLevelInfo
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("elevationMm")] public double ElevationMm { get; set; }
    }

    public class ViewInfo
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("viewType")] public string ViewType { get; set; }
        [JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)] public string Level { get; set; }
        [JsonProperty("scale")] public int Scale { get; set; }
        [JsonProperty("discipline", NullValueHandling = NullValueHandling.Ignore)] public string Discipline { get; set; }
        [JsonProperty("template", NullValueHandling = NullValueHandling.Ignore)] public string Template { get; set; }
        [JsonProperty("sheet", NullValueHandling = NullValueHandling.Ignore)] public string Sheet { get; set; }
    }

    public class SheetInfo
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("number")] public string Number { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("titleBlock", NullValueHandling = NullValueHandling.Ignore)] public string TitleBlock { get; set; }
        [JsonProperty("sizeMm", NullValueHandling = NullValueHandling.Ignore)] public string SizeMm { get; set; }
        [JsonProperty("views")] public List<NamedId> Views { get; set; } = new List<NamedId>();
    }

    public class TypeRef
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)] public string Category { get; set; }
        [JsonProperty("familyName", NullValueHandling = NullValueHandling.Ignore)] public string FamilyName { get; set; }
        [JsonProperty("typeName")] public string TypeName { get; set; }
    }

    public class DocumentationInfo
    {
        [JsonProperty("activeView")] public ViewInfo ActiveView { get; set; }
        [JsonProperty("levels")] public List<DocLevelInfo> Levels { get; set; } = new List<DocLevelInfo>();
        [JsonProperty("views")] public List<ViewInfo> Views { get; set; } = new List<ViewInfo>();
        [JsonProperty("viewTemplates")] public List<NamedId> ViewTemplates { get; set; } = new List<NamedId>();
        [JsonProperty("viewFamilyTypes")] public List<NamedId> ViewFamilyTypes { get; set; } = new List<NamedId>();
        [JsonProperty("sheets")] public List<SheetInfo> Sheets { get; set; } = new List<SheetInfo>();
        [JsonProperty("titleBlocks")] public List<TypeRef> TitleBlocks { get; set; } = new List<TypeRef>();
        [JsonProperty("tagTypes")] public List<TypeRef> TagTypes { get; set; } = new List<TypeRef>();
        [JsonProperty("textNoteTypes")] public List<NamedId> TextNoteTypes { get; set; } = new List<NamedId>();
        [JsonProperty("lineStyles")] public List<string> LineStyles { get; set; } = new List<string>();
        [JsonProperty("wireTypes")] public List<NamedId> WireTypes { get; set; } = new List<NamedId>();
        [JsonProperty("schedules")] public List<NamedId> Schedules { get; set; } = new List<NamedId>();
        [JsonProperty("panelScheduleTemplates")] public List<NamedId> PanelScheduleTemplates { get; set; } = new List<NamedId>();
        [JsonProperty("panels")] public List<NamedId> Panels { get; set; } = new List<NamedId>();
    }

    // ------------------------------------------------------------------ create_views

    public class ViewRequest
    {
        /// <summary>floorPlan | ceilingPlan | drafting | duplicate</summary>
        [JsonProperty("viewType")] public string ViewType { get; set; } = "floorPlan";
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("levelName")] public string LevelName { get; set; }
        /// <summary>ViewFamilyType name (partial), e.g. "Electrical Plan".</summary>
        [JsonProperty("viewFamilyTypeName")] public string ViewFamilyTypeName { get; set; }
        [JsonProperty("templateName")] public string TemplateName { get; set; }
        [JsonProperty("scale")] public int Scale { get; set; }
        /// <summary>electrical | architectural | mechanical | plumbing | structural | coordination</summary>
        [JsonProperty("discipline")] public string Discipline { get; set; }
        /// <summary>Text for the "Sub-Discipline" parameter when the project has it.</summary>
        [JsonProperty("subDiscipline")] public string SubDiscipline { get; set; }
        /// <summary>Crop region corners in model mm (plan views).</summary>
        [JsonProperty("cropMin")] public JZPoint CropMin { get; set; }
        [JsonProperty("cropMax")] public JZPoint CropMax { get; set; }
        /// <summary>For viewType=duplicate.</summary>
        [JsonProperty("sourceViewId")] public long SourceViewId { get; set; } = -1;
        /// <summary>duplicate | withDetailing | dependent</summary>
        [JsonProperty("duplicateOption")] public string DuplicateOption { get; set; } = "duplicate";
        [JsonProperty("open")] public bool Open { get; set; }
    }

    public class CreatedView
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("viewId")] public long ViewId { get; set; } = -1;
        [JsonProperty("viewType", NullValueHandling = NullValueHandling.Ignore)] public string ViewType { get; set; }
        [JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)] public string Level { get; set; }
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public string Error { get; set; }
    }

    // ------------------------------------------------------------------ create_sheets

    public class SheetPlacement
    {
        [JsonProperty("viewId")] public long ViewId { get; set; } = -1;
        [JsonProperty("viewName")] public string ViewName { get; set; }
        /// <summary>Center (views) or top-left corner (schedules) on the sheet, paper mm from the sheet origin. Omit for automatic layout.</summary>
        [JsonProperty("x")] public double? X { get; set; }
        [JsonProperty("y")] public double? Y { get; set; }
        [JsonProperty("viewportTypeName")] public string ViewportTypeName { get; set; }
    }

    public class SheetRequest
    {
        /// <summary>Existing sheet to add views to. Otherwise a new sheet is created.</summary>
        [JsonProperty("sheetId")] public long SheetId { get; set; } = -1;
        [JsonProperty("number")] public string Number { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("titleBlockId")] public long TitleBlockId { get; set; } = -1;
        [JsonProperty("titleBlockName")] public string TitleBlockName { get; set; }
        [JsonProperty("views")] public List<SheetPlacement> Views { get; set; } = new List<SheetPlacement>();
        /// <summary>Schedules and panel schedules (by id or name).</summary>
        [JsonProperty("schedules")] public List<SheetPlacement> Schedules { get; set; } = new List<SheetPlacement>();
        /// <summary>Sheet parameters to set, e.g. "Drawn By".</summary>
        [JsonProperty("parameters")] public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    public class CreatedSheet
    {
        [JsonProperty("sheetId")] public long SheetId { get; set; } = -1;
        [JsonProperty("number")] public string Number { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("placed")] public List<NamedId> Placed { get; set; } = new List<NamedId>();
        [JsonProperty("errors")] public List<string> Errors { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------ tag_elements

    public class TagRequest
    {
        [JsonProperty("viewId")] public long ViewId { get; set; } = -1;
        /// <summary>BuiltInCategory names. OST_Rooms also tags rooms of linked models.</summary>
        [JsonProperty("categories")] public List<string> Categories { get; set; } = new List<string>();
        [JsonProperty("elementIds")] public List<long> ElementIds { get; set; } = new List<long>();
        [JsonProperty("tagTypeId")] public long TagTypeId { get; set; } = -1;
        /// <summary>Partial "family" or "family: type" of the tag.</summary>
        [JsonProperty("tagTypeName")] public string TagTypeName { get; set; }
        [JsonProperty("addLeader")] public bool AddLeader { get; set; }
        /// <summary>Offset of the tag head from the element, paper mm (multiplied by the view scale).</summary>
        [JsonProperty("offsetX")] public double OffsetX { get; set; }
        [JsonProperty("offsetY")] public double OffsetY { get; set; } = 4;
        [JsonProperty("vertical")] public bool Vertical { get; set; }
        [JsonProperty("skipTagged")] public bool SkipTagged { get; set; } = true;
        [JsonProperty("includeLinkedRooms")] public bool IncludeLinkedRooms { get; set; } = true;
    }

    public class TagResult
    {
        [JsonProperty("viewId")] public long ViewId { get; set; }
        [JsonProperty("created")] public int Created { get; set; }
        [JsonProperty("skippedAlreadyTagged")] public int SkippedAlreadyTagged { get; set; }
        [JsonProperty("perCategory")] public Dictionary<string, int> PerCategory { get; set; } = new Dictionary<string, int>();
        [JsonProperty("tagIds")] public List<long> TagIds { get; set; } = new List<long>();
        [JsonProperty("errors")] public List<string> Errors { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------ create_schedule

    public class ScheduleFilterInfo
    {
        [JsonProperty("field")] public string Field { get; set; }
        /// <summary>equal | notEqual | contains | notContains | beginsWith | greater | greaterOrEqual | less | lessOrEqual | hasValue | hasNoValue</summary>
        [JsonProperty("condition")] public string Condition { get; set; } = "equal";
        [JsonProperty("value")] public string Value { get; set; }
    }

    public class ScheduleSortInfo
    {
        [JsonProperty("field")] public string Field { get; set; }
        [JsonProperty("descending")] public bool Descending { get; set; }
        [JsonProperty("header")] public bool Header { get; set; }
        [JsonProperty("footer")] public bool Footer { get; set; }
    }

    public class ScheduleRequest
    {
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>lighting | devices | panels | circuits | conduit | cableTray. Fills category/fields/sort when not given.</summary>
        [JsonProperty("preset")] public string Preset { get; set; }
        [JsonProperty("category")] public string Category { get; set; }
        [JsonProperty("fields")] public List<string> Fields { get; set; } = new List<string>();
        [JsonProperty("filters")] public List<ScheduleFilterInfo> Filters { get; set; } = new List<ScheduleFilterInfo>();
        [JsonProperty("sortBy")] public List<ScheduleSortInfo> SortBy { get; set; } = new List<ScheduleSortInfo>();
        [JsonProperty("itemize")] public bool? Itemize { get; set; }
        [JsonProperty("grandTotal")] public bool GrandTotal { get; set; } = true;
        /// <summary>Field names whose column should be totalled.</summary>
        [JsonProperty("totals")] public List<string> Totals { get; set; } = new List<string>();
        [JsonProperty("sheetId")] public long SheetId { get; set; } = -1;
        [JsonProperty("x")] public double? X { get; set; }
        [JsonProperty("y")] public double? Y { get; set; }
    }

    public class ScheduleResult
    {
        [JsonProperty("scheduleId")] public long ScheduleId { get; set; } = -1;
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("category")] public string Category { get; set; }
        [JsonProperty("fields")] public List<string> Fields { get; set; } = new List<string>();
        [JsonProperty("rows")] public int Rows { get; set; }
        [JsonProperty("availableFields", NullValueHandling = NullValueHandling.Ignore)] public List<string> AvailableFields { get; set; }
        [JsonProperty("sheetInstanceId")] public long SheetInstanceId { get; set; } = -1;
    }

    // ------------------------------------------------------------------ create_panel_schedules

    public class PanelScheduleRequest
    {
        [JsonProperty("panelIds")] public List<long> PanelIds { get; set; } = new List<long>();
        [JsonProperty("panelNames")] public List<string> PanelNames { get; set; } = new List<string>();
        [JsonProperty("all")] public bool All { get; set; }
        [JsonProperty("templateName")] public string TemplateName { get; set; }
        [JsonProperty("sheetId")] public long SheetId { get; set; } = -1;
        /// <summary>Top-left of the first schedule on the sheet, paper mm. Others are stacked to the right.</summary>
        [JsonProperty("x")] public double? X { get; set; }
        [JsonProperty("y")] public double? Y { get; set; }
    }

    public class PanelScheduleResult
    {
        [JsonProperty("schedules")] public List<NamedId> Schedules { get; set; } = new List<NamedId>();
        [JsonProperty("placedOnSheet")] public int PlacedOnSheet { get; set; }
        [JsonProperty("errors")] public List<string> Errors { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------ create_wires

    public class WireRequest
    {
        [JsonProperty("viewId")] public long ViewId { get; set; } = -1;
        [JsonProperty("circuitIds")] public List<long> CircuitIds { get; set; } = new List<long>();
        /// <summary>All circuits of these panels.</summary>
        [JsonProperty("panelNames")] public List<string> PanelNames { get; set; } = new List<string>();
        /// <summary>arc | chamfer</summary>
        [JsonProperty("wiringType")] public string WiringType { get; set; } = "arc";
        [JsonProperty("wireTypeName")] public string WireTypeName { get; set; }
        [JsonProperty("homeRun")] public bool HomeRun { get; set; } = true;
        /// <summary>Home run length in paper mm (multiplied by the view scale).</summary>
        [JsonProperty("homeRunLength")] public double HomeRunLength { get; set; } = 12;
        [JsonProperty("skipCircuitsWithWires")] public bool SkipCircuitsWithWires { get; set; } = true;
    }

    public class WireResult
    {
        [JsonProperty("viewId")] public long ViewId { get; set; }
        [JsonProperty("circuitsWired")] public int CircuitsWired { get; set; }
        [JsonProperty("wires")] public int Wires { get; set; }
        [JsonProperty("skipped")] public List<string> Skipped { get; set; } = new List<string>();
        [JsonProperty("errors")] public List<string> Errors { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------ create_annotations

    public class TextNoteInfo
    {
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("textTypeName")] public string TextTypeName { get; set; }
        /// <summary>Degrees.</summary>
        [JsonProperty("rotation")] public double Rotation { get; set; }
        /// <summary>Wrap width in paper mm; 0 = no wrap.</summary>
        [JsonProperty("width")] public double Width { get; set; }
    }

    public class DetailLineInfo
    {
        [JsonProperty("points")] public List<JZPoint> Points { get; set; } = new List<JZPoint>();
        [JsonProperty("closed")] public bool Closed { get; set; }
        [JsonProperty("lineStyle")] public string LineStyle { get; set; }
    }

    public class DetailCircleInfo
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("radius")] public double Radius { get; set; }
        [JsonProperty("lineStyle")] public string LineStyle { get; set; }
    }

    public class AnnotationRequest
    {
        [JsonProperty("viewId")] public long ViewId { get; set; } = -1;
        [JsonProperty("texts")] public List<TextNoteInfo> Texts { get; set; } = new List<TextNoteInfo>();
        [JsonProperty("lines")] public List<DetailLineInfo> Lines { get; set; } = new List<DetailLineInfo>();
        [JsonProperty("circles")] public List<DetailCircleInfo> Circles { get; set; } = new List<DetailCircleInfo>();
    }

    public class AnnotationResult
    {
        [JsonProperty("viewId")] public long ViewId { get; set; }
        [JsonProperty("textIds")] public List<long> TextIds { get; set; } = new List<long>();
        [JsonProperty("lineIds")] public List<long> LineIds { get; set; } = new List<long>();
        [JsonProperty("errors")] public List<string> Errors { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------ export_views

    public class ExportViewsRequest
    {
        [JsonProperty("viewIds")] public List<long> ViewIds { get; set; } = new List<long>();
        /// <summary>png | pdf</summary>
        [JsonProperty("format")] public string Format { get; set; } = "png";
        [JsonProperty("folder")] public string Folder { get; set; }
        [JsonProperty("fileName")] public string FileName { get; set; }
        [JsonProperty("pixelSize")] public int PixelSize { get; set; } = 2000;
        [JsonProperty("combine")] public bool Combine { get; set; } = true;
    }

    public class ExportViewsResult
    {
        [JsonProperty("folder")] public string Folder { get; set; }
        [JsonProperty("files")] public List<string> Files { get; set; } = new List<string>();
    }
}
