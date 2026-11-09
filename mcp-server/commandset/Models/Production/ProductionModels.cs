using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Production
{
    // ------------------------------------------------------------------ read_cad_link

    public class CadReadRequest
    {
        /// <summary>ImportInstance id. Default: all CAD links/imports (or the one whose name contains Name).</summary>
        [JsonProperty("importId")] public long ImportId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>Only these layers (case-insensitive, partial). Empty: summary of all layers unless Detail is true.</summary>
        [JsonProperty("layers")] public List<string> Layers { get; set; } = new List<string>();
        /// <summary>Return lines/arcs/blocks too. Default: true when layers are given.</summary>
        [JsonProperty("detail")] public bool? Detail { get; set; }
        /// <summary>Model window in mm (plan X/Y).</summary>
        [JsonProperty("minX")] public double? MinX { get; set; }
        [JsonProperty("minY")] public double? MinY { get; set; }
        [JsonProperty("maxX")] public double? MaxX { get; set; }
        [JsonProperty("maxY")] public double? MaxY { get; set; }
        [JsonProperty("maxItems")] public int MaxItems { get; set; } = 1500;
        [JsonProperty("detectWalls")] public bool DetectWalls { get; set; }
        [JsonProperty("wallLayers")] public List<string> WallLayers { get; set; } = new List<string>();
        [JsonProperty("minWallMm")] public double MinWallMm { get; set; } = 80;
        [JsonProperty("maxWallMm")] public double MaxWallMm { get; set; } = 600;
        /// <summary>Also write everything (not limited by maxItems) to this JSON file. Default Documents\RevitClaude\cad\<name>.json.</summary>
        [JsonProperty("outputFile")] public string OutputFile { get; set; }
    }

    public class CadInstanceInfo
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("isLinked")] public bool IsLinked { get; set; }
        [JsonProperty("viewSpecific")] public bool ViewSpecific { get; set; }
        [JsonProperty("ownerView", NullValueHandling = NullValueHandling.Ignore)] public string OwnerView { get; set; }
        [JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)] public string Level { get; set; }
        [JsonProperty("minMm")] public double[] MinMm { get; set; }
        [JsonProperty("maxMm")] public double[] MaxMm { get; set; }
    }

    public class CadLayerInfo
    {
        [JsonProperty("layer")] public string Layer { get; set; }
        [JsonProperty("lines")] public int Lines { get; set; }
        [JsonProperty("arcs")] public int Arcs { get; set; }
        [JsonProperty("polylines")] public int Polylines { get; set; }
        [JsonProperty("blocks")] public int Blocks { get; set; }
        [JsonProperty("lengthM")] public double LengthM { get; set; }
    }

    public class CadCurveInfo
    {
        [JsonProperty("layer")] public string Layer { get; set; }
        /// <summary>line, arc, circle, polyline, curve</summary>
        [JsonProperty("type")] public string Type { get; set; }
        /// <summary>Plan points [x, y] in mm (model coordinates).</summary>
        [JsonProperty("points")] public List<double[]> Points { get; set; } = new List<double[]>();
        [JsonProperty("radiusMm", NullValueHandling = NullValueHandling.Ignore)] public double? RadiusMm { get; set; }
        [JsonProperty("closed", NullValueHandling = NullValueHandling.Ignore)] public bool? Closed { get; set; }
        [JsonProperty("block", NullValueHandling = NullValueHandling.Ignore)] public string Block { get; set; }
    }

    public class CadBlockInfo
    {
        [JsonProperty("layer")] public string Layer { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("rotationDeg")] public double RotationDeg { get; set; }
    }

    public class CadWallInfo
    {
        [JsonProperty("layer")] public string Layer { get; set; }
        [JsonProperty("start")] public double[] Start { get; set; }
        [JsonProperty("end")] public double[] End { get; set; }
        [JsonProperty("thicknessMm")] public double ThicknessMm { get; set; }
        [JsonProperty("lengthMm")] public double LengthMm { get; set; }
    }

    public class CadReadResult
    {
        [JsonProperty("instances")] public List<CadInstanceInfo> Instances { get; set; } = new List<CadInstanceInfo>();
        [JsonProperty("layers")] public List<CadLayerInfo> Layers { get; set; } = new List<CadLayerInfo>();
        [JsonProperty("curves")] public List<CadCurveInfo> Curves { get; set; } = new List<CadCurveInfo>();
        [JsonProperty("blocks")] public List<CadBlockInfo> Blocks { get; set; } = new List<CadBlockInfo>();
        [JsonProperty("walls")] public List<CadWallInfo> Walls { get; set; } = new List<CadWallInfo>();
        [JsonProperty("truncated")] public bool Truncated { get; set; }
        [JsonProperty("file", NullValueHandling = NullValueHandling.Ignore)] public string File { get; set; }
        [JsonProperty("notes")] public List<string> Notes { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------ sheet_point_to_model

    public class SheetPointRequest
    {
        [JsonProperty("sheetId")] public long SheetId { get; set; }
        [JsonProperty("sheetNumber")] public string SheetNumber { get; set; }
        /// <summary>Points on the sheet in paper mm from the bottom-left (e.g. from read_pdf_markups).</summary>
        [JsonProperty("points")] public List<SheetPointInput> Points { get; set; } = new List<SheetPointInput>();
        /// <summary>PDF page size in mm. When given, the page is taken to show the title block exactly (PDF exported from Revit).</summary>
        [JsonProperty("pageWidthMm")] public double PageWidthMm { get; set; }
        [JsonProperty("pageHeightMm")] public double PageHeightMm { get; set; }
        /// <summary>List model elements of the view within this radius of the point (mm, model). 0 = none.</summary>
        [JsonProperty("nearbyRadiusMm")] public double NearbyRadiusMm { get; set; } = 1500;
    }

    public class SheetPointInput
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("xMm")] public double XMm { get; set; }
        [JsonProperty("yMm")] public double YMm { get; set; }
    }

    public class SheetPointResult
    {
        [JsonProperty("sheet")] public string Sheet { get; set; }
        [JsonProperty("points")] public List<SheetPointHit> Points { get; set; } = new List<SheetPointHit>();
    }

    public class SheetPointHit
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("sheetXMm")] public double SheetXMm { get; set; }
        [JsonProperty("sheetYMm")] public double SheetYMm { get; set; }
        [JsonProperty("viewId", NullValueHandling = NullValueHandling.Ignore)] public long? ViewId { get; set; }
        [JsonProperty("viewName", NullValueHandling = NullValueHandling.Ignore)] public string ViewName { get; set; }
        [JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)] public string Level { get; set; }
        /// <summary>Model point [x, y, z] in mm.</summary>
        [JsonProperty("model", NullValueHandling = NullValueHandling.Ignore)] public double[] Model { get; set; }
        [JsonProperty("room", NullValueHandling = NullValueHandling.Ignore)] public string Room { get; set; }
        [JsonProperty("nearby")] public List<NearbyElement> Nearby { get; set; } = new List<NearbyElement>();
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public string Error { get; set; }
    }

    public class NearbyElement
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("category")] public string Category { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("distanceMm")] public double DistanceMm { get; set; }
    }

    // ------------------------------------------------------------------ title blocks

    public class SheetSelection
    {
        /// <summary>Empty sheetIds and sheetNumbers: all sheets.</summary>
        [JsonProperty("sheetIds")] public List<long> SheetIds { get; set; } = new List<long>();
        [JsonProperty("sheetNumbers")] public List<string> SheetNumbers { get; set; } = new List<string>();
    }

    public class TitleBlockFieldsResult
    {
        [JsonProperty("sheetCount")] public int SheetCount { get; set; }
        [JsonProperty("titleBlocks")] public List<string> TitleBlocks { get; set; } = new List<string>();
        [JsonProperty("fields")] public List<TitleBlockField> Fields { get; set; } = new List<TitleBlockField>();
    }

    public class TitleBlockField
    {
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>project (Project Information), sheet, titleblock (instance), titleblock-type</summary>
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("readOnly")] public bool ReadOnly { get; set; }
        /// <summary>Distinct values over the selected sheets (first 5).</summary>
        [JsonProperty("values")] public List<string> Values { get; set; } = new List<string>();
        [JsonProperty("emptyOnSheets")] public int EmptyOnSheets { get; set; }
    }

    public class FillTitleBlocksRequest : SheetSelection
    {
        /// <summary>Values for every selected sheet, by parameter name (Project Information, sheet or title block parameter).</summary>
        [JsonProperty("values")] public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
        /// <summary>Values for single sheets: sheet number -> {name: value}. Override Values.</summary>
        [JsonProperty("perSheet")] public Dictionary<string, Dictionary<string, string>> PerSheet { get; set; } = new Dictionary<string, Dictionary<string, string>>();
        /// <summary>Only fill parameters that are empty now.</summary>
        [JsonProperty("onlyEmpty")] public bool OnlyEmpty { get; set; }
        /// <summary>Fields that must not be empty; reported per sheet after filling.</summary>
        [JsonProperty("requiredFields")] public List<string> RequiredFields { get; set; } = new List<string>();
        /// <summary>Change the title block of the selected sheets to this "Family: Type" (partial).</summary>
        [JsonProperty("titleBlock")] public string TitleBlock { get; set; }
    }

    public class FillTitleBlocksResult
    {
        [JsonProperty("sheets")] public List<FilledSheet> Sheets { get; set; } = new List<FilledSheet>();
        [JsonProperty("projectInfoSet")] public List<string> ProjectInfoSet { get; set; } = new List<string>();
        [JsonProperty("notFound")] public List<string> NotFound { get; set; } = new List<string>();
    }

    public class FilledSheet
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("number")] public string Number { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("set")] public List<string> Set { get; set; } = new List<string>();
        [JsonProperty("missing")] public List<string> Missing { get; set; } = new List<string>();
        [JsonProperty("errors")] public List<string> Errors { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------ place_stamp

    public class PlaceStampRequest : SheetSelection
    {
        [JsonProperty("imagePath")] public string ImagePath { get; set; }
        /// <summary>Centre of the stamp in paper mm from the title block's bottom-left. Default: 60 mm left and 60 mm up from its bottom-right.</summary>
        [JsonProperty("xMm")] public double? XMm { get; set; }
        [JsonProperty("yMm")] public double? YMm { get; set; }
        /// <summary>Measured from the title block's right edge instead of its left edge.</summary>
        [JsonProperty("fromRight")] public bool FromRight { get; set; } = true;
        [JsonProperty("widthMm")] public double WidthMm { get; set; } = 40;
        /// <summary>Replace a stamp placed earlier by this command.</summary>
        [JsonProperty("replace")] public bool Replace { get; set; }
        /// <summary>Who approved, written on the stamp element's Comments.</summary>
        [JsonProperty("approvedBy")] public string ApprovedBy { get; set; }
    }

    public class PlaceStampResult
    {
        [JsonProperty("imageTypeId")] public long ImageTypeId { get; set; }
        [JsonProperty("placed")] public List<StampedSheet> Placed { get; set; } = new List<StampedSheet>();
        [JsonProperty("skipped")] public List<string> Skipped { get; set; } = new List<string>();
    }

    public class StampedSheet
    {
        [JsonProperty("sheet")] public string Sheet { get; set; }
        [JsonProperty("imageId")] public long ImageId { get; set; }
        [JsonProperty("xMm")] public double XMm { get; set; }
        [JsonProperty("yMm")] public double YMm { get; set; }
    }
}
