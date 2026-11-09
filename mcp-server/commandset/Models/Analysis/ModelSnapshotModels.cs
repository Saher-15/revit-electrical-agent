using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Analysis
{
    /// <summary>Input of export_model_snapshot.</summary>
    public class ModelSnapshotRequest
    {
        /// <summary>Folder for the JSON file. Default: Documents\RevitClaude\snapshots.</summary>
        [JsonProperty("outputFolder")]
        public string OutputFolder { get; set; }

        /// <summary>File name without folder. Default: &lt;model title&gt;_&lt;yyyyMMdd_HHmmss&gt;.json.</summary>
        [JsonProperty("fileName")]
        public string FileName { get; set; }

        /// <summary>Only elements, rooms and panels on levels whose name contains this text.</summary>
        [JsonProperty("levelName")]
        public string LevelName { get; set; }

        /// <summary>Also read rooms from loaded Revit links (the architectural model). Default true.</summary>
        [JsonProperty("includeLinkedRooms")]
        public bool IncludeLinkedRooms { get; set; } = true;

        /// <summary>Include room/space outlines (outer loop, mm, host coordinates). Default true.</summary>
        [JsonProperty("includeRoomBoundaries")]
        public bool IncludeRoomBoundaries { get; set; } = true;

        /// <summary>Extra BuiltInCategory names to export as elements, e.g. OST_MechanicalEquipment.</summary>
        [JsonProperty("extraCategories")]
        public List<string> ExtraCategories { get; set; } = new List<string>();

        /// <summary>Dump every instance and type parameter with a value instead of the curated list. Default false (file gets much larger).</summary>
        [JsonProperty("includeAllParameters")]
        public bool IncludeAllParameters { get; set; } = false;
    }

    /// <summary>Small result returned over the socket; the full snapshot goes to the file.</summary>
    public class ModelSnapshotResult
    {
        [JsonProperty("filePath")] public string FilePath { get; set; }
        [JsonProperty("fileSizeKb")] public long FileSizeKb { get; set; }
        [JsonProperty("documentTitle")] public string DocumentTitle { get; set; }
        [JsonProperty("levelCount")] public int LevelCount { get; set; }
        [JsonProperty("linkCount")] public int LinkCount { get; set; }
        [JsonProperty("roomCount")] public int RoomCount { get; set; }
        [JsonProperty("spaceCount")] public int SpaceCount { get; set; }
        [JsonProperty("elementCount")] public int ElementCount { get; set; }
        [JsonProperty("racewayCount")] public int RacewayCount { get; set; }
        [JsonProperty("panelCount")] public int PanelCount { get; set; }
        [JsonProperty("circuitCount")] public int CircuitCount { get; set; }
        [JsonProperty("elementsWithoutRoom")] public int ElementsWithoutRoom { get; set; }
        [JsonProperty("seconds")] public double Seconds { get; set; }
    }

    // ---- Snapshot file schema (schemaVersion 1). Units: mm, m, m2, m3, VA, V, A. ----

    public class ModelSnapshot
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("exportedAt")] public string ExportedAt { get; set; }
        [JsonProperty("revitVersion")] public string RevitVersion { get; set; }
        [JsonProperty("units")] public string Units { get; set; } = "lengths mm (lengthM in m), areas m2, volumes m3, loads VA, voltage V, rating A";
        [JsonProperty("document")] public SnapshotDocument Document { get; set; } = new SnapshotDocument();
        [JsonProperty("levels")] public List<SnapshotLevel> Levels { get; set; } = new List<SnapshotLevel>();
        [JsonProperty("links")] public List<SnapshotLink> Links { get; set; } = new List<SnapshotLink>();
        [JsonProperty("categoryCounts")] public Dictionary<string, int> CategoryCounts { get; set; } = new Dictionary<string, int>();
        [JsonProperty("rooms")] public List<SnapshotRoom> Rooms { get; set; } = new List<SnapshotRoom>();
        [JsonProperty("doors")] public List<SnapshotDoor> Doors { get; set; } = new List<SnapshotDoor>();
        [JsonProperty("elements")] public List<SnapshotElement> Elements { get; set; } = new List<SnapshotElement>();
        [JsonProperty("raceways")] public List<SnapshotRaceway> Raceways { get; set; } = new List<SnapshotRaceway>();
        [JsonProperty("panels")] public List<SnapshotPanel> Panels { get; set; } = new List<SnapshotPanel>();
        [JsonProperty("circuitsWithoutPanel")] public List<SnapshotCircuit> CircuitsWithoutPanel { get; set; } = new List<SnapshotCircuit>();
        [JsonProperty("distributionSystems")] public List<SnapshotDistributionSystem> DistributionSystems { get; set; } = new List<SnapshotDistributionSystem>();
        [JsonProperty("warnings")] public List<string> Warnings { get; set; } = new List<string>();
    }

    public class SnapshotDocument
    {
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("isWorkshared")] public bool IsWorkshared { get; set; }
        [JsonProperty("activeView")] public string ActiveView { get; set; }
        [JsonProperty("projectInfo")] public Dictionary<string, string> ProjectInfo { get; set; } = new Dictionary<string, string>();
    }

    public class SnapshotLevel
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("elevationMm")] public double ElevationMm { get; set; }
    }

    /// <summary>A door from the host model or a loaded link, in host coordinates. Used to keep sockets out of doorways and put switches by doors.</summary>
    public class SnapshotDoor
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("level")] public string Level { get; set; }
        [JsonProperty("family")] public string Family { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("xyzMm")] public double[] XyzMm { get; set; }
        [JsonProperty("widthMm")] public double WidthMm { get; set; }
        /// <summary>Unit vector along the wall (hand direction), [x,y].</summary>
        [JsonProperty("alongWall")] public double[] AlongWall { get; set; }
        /// <summary>Unit vector the door faces, [x,y].</summary>
        [JsonProperty("facing")] public double[] Facing { get; set; }
        [JsonProperty("wallThicknessMm", NullValueHandling = NullValueHandling.Ignore)] public double? WallThicknessMm { get; set; }
        [JsonProperty("fromRoomKey", NullValueHandling = NullValueHandling.Ignore)] public string FromRoomKey { get; set; }
        [JsonProperty("toRoomKey", NullValueHandling = NullValueHandling.Ignore)] public string ToRoomKey { get; set; }
    }

    public class SnapshotLink
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("loaded")] public bool Loaded { get; set; }
        [JsonProperty("documentTitle")] public string DocumentTitle { get; set; }
        [JsonProperty("roomCount")] public int RoomCount { get; set; }
    }

    public class SnapshotRoom
    {
        /// <summary>Unique key used by elements: "host:&lt;id&gt;" or "link&lt;linkId&gt;:&lt;id&gt;".</summary>
        [JsonProperty("key")] public string Key { get; set; }
        /// <summary>"Room" or "Space".</summary>
        [JsonProperty("kind")] public string Kind { get; set; }
        /// <summary>"host" or the link name.</summary>
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("number")] public string Number { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("level")] public string Level { get; set; }
        [JsonProperty("department")] public string Department { get; set; }
        [JsonProperty("areaM2")] public double AreaM2 { get; set; }
        [JsonProperty("perimeterM")] public double PerimeterM { get; set; }
        [JsonProperty("volumeM3")] public double VolumeM3 { get; set; }
        [JsonProperty("heightMm")] public double HeightMm { get; set; }
        [JsonProperty("baseElevationMm")] public double BaseElevationMm { get; set; }
        [JsonProperty("centerMm")] public double[] CenterMm { get; set; }
        /// <summary>Outer boundary as [x,y] points in mm, host coordinates.</summary>
        [JsonProperty("boundaryMm", NullValueHandling = NullValueHandling.Ignore)] public List<double[]> BoundaryMm { get; set; }
        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, string> Parameters { get; set; }
    }

    public class SnapshotElement
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("category")] public string Category { get; set; }
        [JsonProperty("builtInCategory")] public string BuiltInCategory { get; set; }
        [JsonProperty("family")] public string Family { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("typeId")] public long TypeId { get; set; }
        [JsonProperty("level")] public string Level { get; set; }
        [JsonProperty("xyzMm")] public double[] XyzMm { get; set; }
        [JsonProperty("heightAboveLevelMm", NullValueHandling = NullValueHandling.Ignore)] public double? HeightAboveLevelMm { get; set; }
        [JsonProperty("hostId", NullValueHandling = NullValueHandling.Ignore)] public long? HostId { get; set; }
        [JsonProperty("hostCategory", NullValueHandling = NullValueHandling.Ignore)] public string HostCategory { get; set; }
        /// <summary>Key of the room (host or linked) containing the element; see SnapshotRoom.Key.</summary>
        [JsonProperty("roomKey", NullValueHandling = NullValueHandling.Ignore)] public string RoomKey { get; set; }
        [JsonProperty("spaceKey", NullValueHandling = NullValueHandling.Ignore)] public string SpaceKey { get; set; }
        [JsonProperty("panel", NullValueHandling = NullValueHandling.Ignore)] public string Panel { get; set; }
        [JsonProperty("circuitNumber", NullValueHandling = NullValueHandling.Ignore)] public string CircuitNumber { get; set; }
        /// <summary>True when the element has an electrical connector but is on no circuit.</summary>
        [JsonProperty("unassigned", NullValueHandling = NullValueHandling.Ignore)] public bool? Unassigned { get; set; }
        [JsonProperty("parameters")] public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    public class SnapshotRaceway
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("category")] public string Category { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("typeId")] public long TypeId { get; set; }
        [JsonProperty("level")] public string Level { get; set; }
        [JsonProperty("lengthM")] public double LengthM { get; set; }
        [JsonProperty("size")] public string Size { get; set; }
        [JsonProperty("widthMm", NullValueHandling = NullValueHandling.Ignore)] public double? WidthMm { get; set; }
        [JsonProperty("heightMm", NullValueHandling = NullValueHandling.Ignore)] public double? HeightMm { get; set; }
        [JsonProperty("diameterMm", NullValueHandling = NullValueHandling.Ignore)] public double? DiameterMm { get; set; }
        [JsonProperty("serviceType", NullValueHandling = NullValueHandling.Ignore)] public string ServiceType { get; set; }
        [JsonProperty("startMm")] public double[] StartMm { get; set; }
        [JsonProperty("endMm")] public double[] EndMm { get; set; }
        [JsonProperty("roomKey", NullValueHandling = NullValueHandling.Ignore)] public string RoomKey { get; set; }
        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, string> Parameters { get; set; }
    }

    public class SnapshotPanel
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("family")] public string Family { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("level")] public string Level { get; set; }
        [JsonProperty("roomKey", NullValueHandling = NullValueHandling.Ignore)] public string RoomKey { get; set; }
        [JsonProperty("distributionSystem")] public string DistributionSystem { get; set; }
        /// <summary>Panel this one is fed from, if any.</summary>
        [JsonProperty("fedFrom", NullValueHandling = NullValueHandling.Ignore)] public string FedFrom { get; set; }
        [JsonProperty("totalApparentLoadVA")] public double TotalApparentLoadVA { get; set; }
        [JsonProperty("parameters")] public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        [JsonProperty("circuits")] public List<SnapshotCircuit> Circuits { get; set; } = new List<SnapshotCircuit>();
    }

    public class SnapshotCircuit
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("circuitNumber")] public string CircuitNumber { get; set; }
        [JsonProperty("panel")] public string Panel { get; set; }
        [JsonProperty("systemType")] public string SystemType { get; set; }
        [JsonProperty("loadName")] public string LoadName { get; set; }
        [JsonProperty("voltage")] public double Voltage { get; set; }
        [JsonProperty("poles")] public int Poles { get; set; }
        [JsonProperty("ratingA")] public double RatingA { get; set; }
        [JsonProperty("apparentLoadVA")] public double ApparentLoadVA { get; set; }
        [JsonProperty("trueLoadW", NullValueHandling = NullValueHandling.Ignore)] public double? TrueLoadW { get; set; }
        [JsonProperty("startSlot")] public int StartSlot { get; set; }
        [JsonProperty("lengthM")] public double LengthM { get; set; }
        [JsonProperty("wireSize", NullValueHandling = NullValueHandling.Ignore)] public string WireSize { get; set; }
        [JsonProperty("elementIds")] public List<long> ElementIds { get; set; } = new List<long>();
        /// <summary>When the circuit feeds another panel, its name.</summary>
        [JsonProperty("feedsPanel", NullValueHandling = NullValueHandling.Ignore)] public string FeedsPanel { get; set; }
        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, string> Parameters { get; set; }
    }

    public class SnapshotDistributionSystem
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("phase")] public string Phase { get; set; }
        [JsonProperty("wires")] public int Wires { get; set; }
        [JsonProperty("lineToLineV", NullValueHandling = NullValueHandling.Ignore)] public double? LineToLineV { get; set; }
        [JsonProperty("lineToGroundV", NullValueHandling = NullValueHandling.Ignore)] public double? LineToGroundV { get; set; }
    }
}
