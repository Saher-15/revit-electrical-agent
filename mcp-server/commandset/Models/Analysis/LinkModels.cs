using System.Collections.Generic;
using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Analysis
{
    public class ManageLinksRequest
    {
        /// <summary>"list" (default) or "reload".</summary>
        [JsonProperty("action")]
        public string Action { get; set; } = "list";

        /// <summary>Only links whose name contains one of these texts (case-insensitive). Empty = all.</summary>
        [JsonProperty("names")]
        public List<string> Names { get; set; } = new List<string>();

        /// <summary>Reload only links that are not loaded (default true); false reloads every matching link.</summary>
        [JsonProperty("onlyUnloaded")]
        public bool OnlyUnloaded { get; set; } = true;

        /// <summary>Folder searched for a local .rvt with the link's file name; found files are loaded from there instead of the stored path.</summary>
        [JsonProperty("localFolder")]
        public string LocalFolder { get; set; }
    }

    public class LinkInfo
    {
        [JsonProperty("typeId")] public long TypeId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("loaded")] public bool Loaded { get; set; }
        /// <summary>Revit LinkedFileStatus: Loaded, Unloaded, NotFound, LocallyUnloaded, InClosedWorkset, Invalid.</summary>
        [JsonProperty("status")] public string Status { get; set; }
        /// <summary>"cloud", "file", "server" or "unknown".</summary>
        [JsonProperty("pathType")] public string PathType { get; set; }
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("instances")] public int Instances { get; set; }
        /// <summary>For action reload: "reloaded", "loadedFromLocal", "skipped" or "failed".</summary>
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
    }

    public class ManageLinksResult
    {
        [JsonProperty("links")] public List<LinkInfo> Links { get; set; } = new List<LinkInfo>();
        [JsonProperty("loaded")] public int Loaded { get; set; }
        [JsonProperty("notLoaded")] public int NotLoaded { get; set; }
        [JsonProperty("reloaded")] public int Reloaded { get; set; }
        [JsonProperty("failed")] public int Failed { get; set; }
    }
}
