using Newtonsoft.Json;
using RevitMCPCommandSet.Models.Common;

namespace RevitMCPCommandSet.Models.Electrical
{
    /// <summary>
    /// One electrical family instance to place (fixture, device, panel...).
    /// All lengths are in millimeters.
    /// </summary>
    public class ElectricalPlacementInfo
    {
        /// <summary>Free text used in messages (e.g. "corridor downlight").</summary>
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        /// <summary>FamilySymbol ElementId. Preferred way to choose the type.</summary>
        [JsonProperty("typeId")]
        public long TypeId { get; set; } = -1;

        /// <summary>Alternative to typeId: partial family name match (case-insensitive).</summary>
        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        /// <summary>Alternative to typeId: partial type name match (case-insensitive).</summary>
        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        /// <summary>BuiltInCategory name used when searching by name (e.g. OST_LightingFixtures).</summary>
        [JsonProperty("category")]
        public string Category { get; set; }

        /// <summary>Placement point. Z is absolute elevation in mm (project origin).</summary>
        [JsonProperty("location")]
        public JZPoint Location { get; set; }

        /// <summary>
        /// ceiling | wall | floor | level | plane | auto.
        /// ceiling: hosted on the ceiling/floor face above the point.
        /// wall: hosted on the nearest wall face/wall.
        /// floor: hosted on the floor face below the point.
        /// level: level-based placement, Z becomes "Elevation from Level".
        /// plane: hosted on a horizontal reference plane at Z (works without a ceiling).
        /// auto: pick from the family placement type.
        /// </summary>
        [JsonProperty("hostMode")]
        public string HostMode { get; set; } = "auto";

        /// <summary>Optional explicit host element (ceiling, wall, floor) id.</summary>
        [JsonProperty("hostElementId")]
        public long HostElementId { get; set; } = -1;

        /// <summary>Optional level name. Defaults to the nearest level below Z.</summary>
        [JsonProperty("levelName")]
        public string LevelName { get; set; }

        /// <summary>
        /// Level to write into "Schedule Level" after placement. Default: the resolved level. Face-hosted elements on a linked
        /// wall or ceiling otherwise get no level and drop out of per-floor schedules and the BOQ. Set to "" to leave it alone.
        /// </summary>
        [JsonProperty("scheduleLevel")]
        public string ScheduleLevel { get; set; }

        /// <summary>Rotation around the vertical axis in degrees.</summary>
        [JsonProperty("rotation")]
        public double Rotation { get; set; }

        /// <summary>Instance parameters to set after placement (name -> value as text).</summary>
        [JsonProperty("parameters")]
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

        /// <summary>Appended to the parameter's current value with " | " (provenance tags keep the designer's text).</summary>
        [JsonProperty("appendParameters")]
        public Dictionary<string, string> AppendParameters { get; set; } = new Dictionary<string, string>();

        /// <summary>User workset name for the new element (workshared models only).</summary>
        [JsonProperty("workset")]
        public string Workset { get; set; }
    }

    public class PlacedElementInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("elementId")] public long ElementId { get; set; } = -1;
        [JsonProperty("familyName")] public string FamilyName { get; set; }
        [JsonProperty("typeName")] public string TypeName { get; set; }
        [JsonProperty("hostMode")] public string HostMode { get; set; }
        [JsonProperty("hostElementId")] public long HostElementId { get; set; } = -1;
        [JsonProperty("scheduleLevel", NullValueHandling = NullValueHandling.Ignore)] public string ScheduleLevel { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
    }

    /// <summary>A polyline run of cable tray or conduit. All lengths in millimeters.</summary>
    public class RacewayRunInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        /// <summary>CableTrayType or ConduitType ElementId. -1 picks the first available type.</summary>
        [JsonProperty("typeId")]
        public long TypeId { get; set; } = -1;

        /// <summary>Partial type name match when typeId is not given.</summary>
        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        /// <summary>Polyline points (at least 2). Z is absolute elevation in mm.</summary>
        [JsonProperty("points")]
        public List<JZPoint> Points { get; set; } = new List<JZPoint>();

        /// <summary>Cable tray width (mm).</summary>
        [JsonProperty("width")]
        public double Width { get; set; }

        /// <summary>Cable tray height (mm).</summary>
        [JsonProperty("height")]
        public double Height { get; set; }

        /// <summary>Conduit outside diameter (mm).</summary>
        [JsonProperty("diameter")]
        public double Diameter { get; set; }

        [JsonProperty("levelName")]
        public string LevelName { get; set; }

        /// <summary>Insert elbow fittings between consecutive segments (default true).</summary>
        [JsonProperty("addFittings")]
        public bool AddFittings { get; set; } = true;
    }

    public class RacewayRunResult
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("segmentIds")] public List<long> SegmentIds { get; set; } = new List<long>();
        [JsonProperty("fittingIds")] public List<long> FittingIds { get; set; } = new List<long>();
        [JsonProperty("typeName")] public string TypeName { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
    }

    public class CircuitCreationInfo
    {
        /// <summary>Devices/fixtures to put on the circuit.</summary>
        [JsonProperty("elementIds")]
        public List<long> ElementIds { get; set; } = new List<long>();

        /// <summary>Panel (electrical equipment) to connect the circuit to. Optional.</summary>
        [JsonProperty("panelId")]
        public long PanelId { get; set; } = -1;

        /// <summary>Partial panel name when panelId is not given.</summary>
        [JsonProperty("panelName")]
        public string PanelName { get; set; }

        /// <summary>PowerCircuit (default) | Data | Telephone | Security | FireAlarm | NurseCall | Controls | Communication</summary>
        [JsonProperty("systemType")]
        public string SystemType { get; set; } = "PowerCircuit";

        /// <summary>Optional "Load Name" for the circuit.</summary>
        [JsonProperty("loadName")]
        public string LoadName { get; set; }

        /// <summary>
        /// Distribution system to assign to the panel when it has none yet (e.g. "120/208 Wye").
        /// When empty, one matching the circuit voltage and poles is picked automatically.
        /// </summary>
        [JsonProperty("distributionSystem")]
        public string DistributionSystem { get; set; }
    }

    public class CircuitInfo
    {
        [JsonProperty("circuitId")] public long CircuitId { get; set; } = -1;
        [JsonProperty("circuitNumber")] public string CircuitNumber { get; set; }
        [JsonProperty("panelName")] public string PanelName { get; set; }
        [JsonProperty("panelId")] public long PanelId { get; set; } = -1;
        [JsonProperty("systemType")] public string SystemType { get; set; }
        [JsonProperty("loadName")] public string LoadName { get; set; }
        [JsonProperty("voltage")] public double Voltage { get; set; }
        [JsonProperty("apparentLoadVA")] public double ApparentLoadVA { get; set; }
        [JsonProperty("elementCount")] public int ElementCount { get; set; }
        [JsonProperty("elementIds")] public List<long> ElementIds { get; set; } = new List<long>();
        [JsonProperty("error")] public string Error { get; set; }
    }

    public class PanelInfo
    {
        [JsonProperty("panelId")] public long PanelId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("familyName")] public string FamilyName { get; set; }
        [JsonProperty("typeName")] public string TypeName { get; set; }
        [JsonProperty("level")] public string Level { get; set; }
        [JsonProperty("distributionSystem")] public string DistributionSystem { get; set; }
        [JsonProperty("circuitCount")] public int CircuitCount { get; set; }
        [JsonProperty("totalApparentLoadVA")] public double TotalApparentLoadVA { get; set; }
        [JsonProperty("circuits")] public List<CircuitInfo> Circuits { get; set; } = new List<CircuitInfo>();
    }

    public class ElectricalSummaryRequest
    {
        /// <summary>Include each panel's circuit list (can be large). Default false.</summary>
        [JsonProperty("includeCircuits")]
        public bool IncludeCircuits { get; set; } = false;

        /// <summary>Include element ids inside each circuit. Default false.</summary>
        [JsonProperty("includeCircuitElementIds")]
        public bool IncludeCircuitElementIds { get; set; } = false;

        /// <summary>Only panels whose name contains this text.</summary>
        [JsonProperty("panelNameFilter")]
        public string PanelNameFilter { get; set; }

        /// <summary>Only panels on this level (name).</summary>
        [JsonProperty("levelName")]
        public string LevelName { get; set; }

        [JsonProperty("maxPanels")]
        public int MaxPanels { get; set; } = 50;

        [JsonProperty("maxUnassignedIds")]
        public int MaxUnassignedIds { get; set; } = 50;
    }

    public class ElectricalSummary
    {
        [JsonProperty("documentTitle")]
        public string DocumentTitle { get; set; }

        [JsonProperty("panelCountTotal")]
        public int PanelCountTotal { get; set; }

        [JsonProperty("circuitCountTotal")]
        public int CircuitCountTotal { get; set; }

        [JsonProperty("elementCountsByCategory")]
        public Dictionary<string, int> ElementCountsByCategory { get; set; } = new Dictionary<string, int>();

        [JsonProperty("panels")]
        public List<PanelInfo> Panels { get; set; } = new List<PanelInfo>();

        [JsonProperty("unassignedDeviceCount")]
        public int UnassignedDeviceCount { get; set; }

        [JsonProperty("unassignedDeviceIds")]
        public List<long> UnassignedDeviceIds { get; set; } = new List<long>();

        [JsonProperty("cableTrayTypes")]
        public List<FamilyTypeInfo> CableTrayTypes { get; set; } = new List<FamilyTypeInfo>();

        [JsonProperty("conduitTypes")]
        public List<FamilyTypeInfo> ConduitTypes { get; set; } = new List<FamilyTypeInfo>();

        [JsonProperty("distributionSystems")]
        public List<string> DistributionSystems { get; set; } = new List<string>();
    }

    public class LoadFamilyInfo
    {
        /// <summary>Full path to an .rfa file. Optional if nameFilter is given.</summary>
        [JsonProperty("filePath")]
        public string FilePath { get; set; }

        /// <summary>Partial file name to search for in the Revit libraries (case-insensitive).</summary>
        [JsonProperty("nameFilter")]
        public string NameFilter { get; set; }

        /// <summary>When several files match, load all of them (default false: list them instead).</summary>
        [JsonProperty("loadAllMatches")]
        public bool LoadAllMatches { get; set; }

        [JsonProperty("maxResults")]
        public int MaxResults { get; set; } = 20;
    }

    public class LoadFamilyResult
    {
        [JsonProperty("loadedFamilies")] public List<string> LoadedFamilies { get; set; } = new List<string>();
        [JsonProperty("loadedTypes")] public List<FamilyTypeInfo> LoadedTypes { get; set; } = new List<FamilyTypeInfo>();
        [JsonProperty("candidates")] public List<string> Candidates { get; set; } = new List<string>();
    }
}
