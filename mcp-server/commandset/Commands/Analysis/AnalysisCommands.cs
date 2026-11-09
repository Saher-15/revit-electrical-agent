using Autodesk.Revit.UI;
using RevitMCPCommandSet.Commands.Electrical;
using RevitMCPCommandSet.Models.Analysis;
using RevitMCPCommandSet.Services.Analysis;

namespace RevitMCPCommandSet.Commands.Analysis
{
    /// <summary>Read-only model analysis commands. They reuse the silent electrical command plumbing.</summary>
    public class ExportModelSnapshotCommand : ElectricalCommandBase<ModelSnapshotRequest, ModelSnapshotResult>
    {
        public override string CommandName => "export_model_snapshot";
        protected override string InputProperty => null;
        // The MCP client gives up after 120 s, so stop waiting a little earlier.
        public ExportModelSnapshotCommand(UIApplication uiApp) : base(new ExportModelSnapshotEventHandler(), uiApp, 115000) { }
    }
}

namespace RevitMCPCommandSet.Commands.Analysis
{
    public class ManageLinksCommand : ElectricalCommandBase<ManageLinksRequest, ManageLinksResult>
    {
        public override string CommandName => "manage_links";
        protected override string InputProperty => null;
        public ManageLinksCommand(UIApplication uiApp) : base(new ManageLinksEventHandler(), uiApp, 115000) { }
    }
}
