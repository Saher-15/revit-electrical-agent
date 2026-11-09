using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Documentation
{
    // ------------------------------------------------------------------ copy_level_elements

    public class CopyLevelRequest
    {
        [JsonProperty("sourceLevel")] public string SourceLevel { get; set; }
        [JsonProperty("targetLevels")] public List<string> TargetLevels { get; set; } = new List<string>();
        /// <summary>BuiltInCategory names. Default: the electrical device categories.</summary>
        [JsonProperty("categories")] public List<string> Categories { get; set; } = new List<string>();
        /// <summary>Copies for real, reports, then rolls everything back.</summary>
        [JsonProperty("dryRun")] public bool DryRun { get; set; } = true;
        [JsonProperty("allowCloudModel")] public bool AllowCloudModel { get; set; }
    }

    public class CopyLevelTargetResult
    {
        [JsonProperty("level")] public string Level { get; set; }
        [JsonProperty("copied")] public int Copied { get; set; }
        [JsonProperty("skippedAlreadyThere")] public int SkippedAlreadyThere { get; set; }
        [JsonProperty("onTargetLevel")] public int OnTargetLevel { get; set; }
        [JsonProperty("wrongHeight")] public int WrongHeight { get; set; }
        [JsonProperty("sourceWithHostFace")] public int SourceWithHostFace { get; set; }
        [JsonProperty("copiesWithHostFace")] public int CopiesWithHostFace { get; set; }
        [JsonProperty("rolledBack")] public bool RolledBack { get; set; }
        [JsonProperty("messages")] public List<string> Messages { get; set; } = new List<string>();
    }

    public class CopyLevelResult
    {
        [JsonProperty("dryRun")] public bool DryRun { get; set; }
        [JsonProperty("sourceLevel")] public string SourceLevel { get; set; }
        [JsonProperty("sourceElements")] public int SourceElements { get; set; }
        [JsonProperty("targets")] public List<CopyLevelTargetResult> Targets { get; set; } = new List<CopyLevelTargetResult>();
    }
}
