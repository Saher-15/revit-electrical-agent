using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Electrical
{
    public class RehostRequest
    {
        /// <summary>BuiltInCategory names. Default: the electrical device categories.</summary>
        [JsonProperty("categories")] public List<string> Categories { get; set; } = new List<string>();
        /// <summary>Only instances whose level has this exact name.</summary>
        [JsonProperty("levelName")] public string LevelName { get; set; }
        /// <summary>Only these element ids (overrides categories/level).</summary>
        [JsonProperty("elementIds")] public List<long> ElementIds { get; set; } = new List<long>();
        /// <summary>Re-host for real, report, then roll everything back.</summary>
        [JsonProperty("dryRun")] public bool DryRun { get; set; } = true;
        [JsonProperty("allowCloudModel")] public bool AllowCloudModel { get; set; }
        /// <summary>Elements per call; the client gives up after 120 s.</summary>
        [JsonProperty("maxElements")] public int MaxElements { get; set; } = 150;
    }

    public class RehostItem
    {
        [JsonProperty("oldId")] public long OldId { get; set; }
        [JsonProperty("newId", NullValueHandling = NullValueHandling.Ignore)] public long? NewId { get; set; }
        [JsonProperty("family")] public string Family { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)] public string Level { get; set; }
        [JsonProperty("circuits")] public int Circuits { get; set; }
        [JsonProperty("tags")] public int Tags { get; set; }
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public string Error { get; set; }
    }

    public class RehostResult
    {
        [JsonProperty("dryRun")] public bool DryRun { get; set; }
        [JsonProperty("candidates")] public int Candidates { get; set; }
        [JsonProperty("processed")] public int Processed { get; set; }
        [JsonProperty("remaining")] public int Remaining { get; set; }
        [JsonProperty("rehosted")] public int Rehosted { get; set; }
        [JsonProperty("failed")] public int Failed { get; set; }
        [JsonProperty("planesCreated")] public int PlanesCreated { get; set; }
        [JsonProperty("circuitsMoved")] public int CircuitsMoved { get; set; }
        [JsonProperty("tagsRecreated")] public int TagsRecreated { get; set; }
        [JsonProperty("rolledBack")] public bool RolledBack { get; set; }
        [JsonProperty("byFamily")] public Dictionary<string, int> ByFamily { get; set; } = new Dictionary<string, int>();
        [JsonProperty("items")] public List<RehostItem> Items { get; set; } = new List<RehostItem>();
    }
}
