using Newtonsoft.Json;
using RevitMCPCommandSet.Models.Common;

namespace RevitMCPCommandSet.Models.Families
{
    /// <summary>A solid in the family. Millimeters; x/y/z is the center of the solid's base.</summary>
    public class FamilySolidInfo
    {
        /// <summary>box | cylinder</summary>
        [JsonProperty("shape")] public string Shape { get; set; } = "box";
        [JsonProperty("width")] public double Width { get; set; } = 100;
        [JsonProperty("depth")] public double Depth { get; set; } = 100;
        [JsonProperty("height")] public double Height { get; set; } = 50;
        [JsonProperty("diameter")] public double Diameter { get; set; } = 100;
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
        /// <summary>Extrude downwards from z (ceiling fixtures hanging below the host face).</summary>
        [JsonProperty("downward")] public bool Downward { get; set; }
        [JsonProperty("material")] public string Material { get; set; }
        /// <summary>Show this solid in plan/RCP. False when a 2D symbol represents the family in plan.</summary>
        [JsonProperty("visibleInPlan")] public bool VisibleInPlan { get; set; } = true;
    }

    public class SymbolLineInfo
    {
        [JsonProperty("x1")] public double X1 { get; set; }
        [JsonProperty("y1")] public double Y1 { get; set; }
        [JsonProperty("x2")] public double X2 { get; set; }
        [JsonProperty("y2")] public double Y2 { get; set; }
    }

    public class SymbolCircleInfo
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("radius")] public double Radius { get; set; }
        /// <summary>Degrees; omit for a full circle.</summary>
        [JsonProperty("startAngle")] public double? StartAngle { get; set; }
        [JsonProperty("endAngle")] public double? EndAngle { get; set; }
    }

    /// <summary>2D plan symbol drawn with symbolic lines (visible only in plan/RCP). Millimeters at 1:1.</summary>
    public class PlanSymbolInfo
    {
        [JsonProperty("lines")] public List<SymbolLineInfo> Lines { get; set; } = new List<SymbolLineInfo>();
        [JsonProperty("circles")] public List<SymbolCircleInfo> Circles { get; set; } = new List<SymbolCircleInfo>();
        /// <summary>Elevation of the symbol plane in mm (0 = family origin).</summary>
        [JsonProperty("z")] public double Z { get; set; }
    }

    public class FamilyParameterInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>length | number | integer | text | yesno | angle | voltage | current | apparentPower | power | wattage | luminousFlux | colorTemperature | efficacy | poles | material</summary>
        [JsonProperty("type")] public string Type { get; set; } = "text";
        [JsonProperty("isInstance")] public bool IsInstance { get; set; }
        /// <summary>electrical | electricalLoads | lighting | identity | dimensions | data | text | constraints | general | materials</summary>
        [JsonProperty("group")] public string Group { get; set; }
        /// <summary>Value for every type (types[].values overrides it).</summary>
        [JsonProperty("value")] public string Value { get; set; }
        [JsonProperty("formula")] public string Formula { get; set; }
    }

    public class FamilyConnectorInfo
    {
        /// <summary>power | powerUnbalanced | data | telephone | fireAlarm | security | communication | controls | nurseCall</summary>
        [JsonProperty("systemType")] public string SystemType { get; set; } = "power";
        [JsonProperty("voltage")] public double? Voltage { get; set; }
        [JsonProperty("poles")] public int? Poles { get; set; }
        [JsonProperty("apparentLoad")] public double? ApparentLoad { get; set; }
        [JsonProperty("powerFactor")] public double? PowerFactor { get; set; }
        /// <summary>Face of the first solid: top | bottom | front | back | left | right.</summary>
        [JsonProperty("face")] public string Face { get; set; } = "top";
        /// <summary>Create family parameters Voltage / Number of Poles / Apparent Load and drive the connector from them so each type can differ.</summary>
        [JsonProperty("linkToParameters")] public bool LinkToParameters { get; set; } = true;
    }

    public class FamilyTypeValues
    {
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>Rename this type (edit_family).</summary>
        [JsonProperty("newName")] public string NewName { get; set; }
        [JsonProperty("values")] public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    public class CreateFamilyRequest
    {
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>Template file name (partial, e.g. "Electrical Fixture", "Lighting Fixture ceiling based", "Electrical Equipment") or full .rft path.</summary>
        [JsonProperty("template")] public string Template { get; set; } = "Generic Model";
        /// <summary>Change the family category (BuiltInCategory name), e.g. OST_ElectricalFixtures.</summary>
        [JsonProperty("category")] public string Category { get; set; }
        /// <summary>Part type for electrical equipment: panelboard | switchboard | transformer | otherPanel | equipmentSwitch | junctionBox | normal</summary>
        [JsonProperty("partType")] public string PartType { get; set; }
        [JsonProperty("solids")] public List<FamilySolidInfo> Solids { get; set; } = new List<FamilySolidInfo>();
        [JsonProperty("planSymbol")] public PlanSymbolInfo PlanSymbol { get; set; }
        [JsonProperty("parameters")] public List<FamilyParameterInfo> Parameters { get; set; } = new List<FamilyParameterInfo>();
        [JsonProperty("connector")] public FamilyConnectorInfo Connector { get; set; }
        [JsonProperty("types")] public List<FamilyTypeValues> Types { get; set; } = new List<FamilyTypeValues>();
        /// <summary>Folder for the .rfa. Default Documents\RevitClaude\families.</summary>
        [JsonProperty("saveFolder")] public string SaveFolder { get; set; }
        [JsonProperty("overwrite")] public bool Overwrite { get; set; }
        [JsonProperty("loadIntoProject")] public bool LoadIntoProject { get; set; } = true;
    }

    public class FamilyParameterState
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("isInstance")] public bool IsInstance { get; set; }
        [JsonProperty("isShared")] public bool IsShared { get; set; }
        [JsonProperty("formula", NullValueHandling = NullValueHandling.Ignore)] public string Formula { get; set; }
        /// <summary>type name -> value as text.</summary>
        [JsonProperty("values")] public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    public class FamilyResult
    {
        [JsonProperty("familyName")] public string FamilyName { get; set; }
        [JsonProperty("familyId")] public long FamilyId { get; set; } = -1;
        [JsonProperty("category")] public string Category { get; set; }
        [JsonProperty("filePath", NullValueHandling = NullValueHandling.Ignore)] public string FilePath { get; set; }
        [JsonProperty("template", NullValueHandling = NullValueHandling.Ignore)] public string Template { get; set; }
        [JsonProperty("placement", NullValueHandling = NullValueHandling.Ignore)] public string Placement { get; set; }
        [JsonProperty("connectors")] public int Connectors { get; set; }
        [JsonProperty("projectTypes")] public List<FamilyTypeInfo> ProjectTypes { get; set; } = new List<FamilyTypeInfo>();
        [JsonProperty("parameters")] public List<FamilyParameterState> Parameters { get; set; } = new List<FamilyParameterState>();
        [JsonProperty("templateCandidates", NullValueHandling = NullValueHandling.Ignore)] public List<string> TemplateCandidates { get; set; }
    }

    public class EditFamilyRequest
    {
        [JsonProperty("familyId")] public long FamilyId { get; set; } = -1;
        [JsonProperty("familyName")] public string FamilyName { get; set; }
        [JsonProperty("addParameters")] public List<FamilyParameterInfo> AddParameters { get; set; } = new List<FamilyParameterInfo>();
        /// <summary>Types to create or change (by name). Missing types are created.</summary>
        [JsonProperty("types")] public List<FamilyTypeValues> Types { get; set; } = new List<FamilyTypeValues>();
        [JsonProperty("deleteTypes")] public List<string> DeleteTypes { get; set; } = new List<string>();
        /// <summary>parameter name -> formula ("" removes the formula).</summary>
        [JsonProperty("formulas")] public Dictionary<string, string> Formulas { get; set; } = new Dictionary<string, string>();
        /// <summary>Add a connector when the family has none (needs a solid), or update the first electrical connector.</summary>
        [JsonProperty("connector")] public FamilyConnectorInfo Connector { get; set; }
        [JsonProperty("partType")] public string PartType { get; set; }
        /// <summary>Also save the edited family as .rfa in this folder.</summary>
        [JsonProperty("saveFolder")] public string SaveFolder { get; set; }
    }

    /// <summary>Create a project type by duplicating one, and/or set type parameters.</summary>
    public class FamilyTypeRequest
    {
        [JsonProperty("typeId")] public long TypeId { get; set; } = -1;
        [JsonProperty("category")] public string Category { get; set; }
        [JsonProperty("familyName")] public string FamilyName { get; set; }
        [JsonProperty("typeName")] public string TypeName { get; set; }
        /// <summary>When given, the type is duplicated under this name first.</summary>
        [JsonProperty("newName")] public string NewName { get; set; }
        [JsonProperty("values")] public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    public class FamilyTypeResult
    {
        [JsonProperty("typeId")] public long TypeId { get; set; } = -1;
        [JsonProperty("familyName")] public string FamilyName { get; set; }
        [JsonProperty("typeName")] public string TypeName { get; set; }
        [JsonProperty("created")] public bool Created { get; set; }
        [JsonProperty("errors")] public List<string> Errors { get; set; } = new List<string>();
    }
}
