using Autodesk.Revit.UI;
using RevitMCPCommandSet.Commands.Electrical;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Documentation;

namespace RevitMCPCommandSet.Commands.Documentation
{
    // Drawing and documentation commands. They reuse the silent electrical command plumbing.

    public class GetDocumentationInfoCommand : ElectricalCommandBase<DocumentationInfoRequest, DocumentationInfo>
    {
        public override string CommandName => "get_documentation_info";
        protected override string InputProperty => null;
        public GetDocumentationInfoCommand(UIApplication uiApp) : base(new GetDocumentationInfoEventHandler(), uiApp, 30000) { }
    }

    public class CreateViewsCommand : ElectricalCommandBase<List<ViewRequest>, List<CreatedView>>
    {
        public override string CommandName => "create_views";
        protected override string InputProperty => "data";
        public CreateViewsCommand(UIApplication uiApp) : base(new CreateViewsEventHandler(), uiApp) { }
    }

    public class CreateSheetsCommand : ElectricalCommandBase<List<SheetRequest>, List<CreatedSheet>>
    {
        public override string CommandName => "create_sheets";
        protected override string InputProperty => "data";
        public CreateSheetsCommand(UIApplication uiApp) : base(new CreateSheetsEventHandler(), uiApp, 90000) { }
    }

    public class TagElementsCommand : ElectricalCommandBase<TagRequest, TagResult>
    {
        public override string CommandName => "tag_elements";
        protected override string InputProperty => null;
        public TagElementsCommand(UIApplication uiApp) : base(new TagElementsEventHandler(), uiApp, 90000) { }
    }

    public class CreateScheduleCommand : ElectricalCommandBase<ScheduleRequest, ScheduleResult>
    {
        public override string CommandName => "create_schedule";
        protected override string InputProperty => null;
        public CreateScheduleCommand(UIApplication uiApp) : base(new CreateScheduleEventHandler(), uiApp) { }
    }

    public class CreatePanelSchedulesCommand : ElectricalCommandBase<PanelScheduleRequest, PanelScheduleResult>
    {
        public override string CommandName => "create_panel_schedules";
        protected override string InputProperty => null;
        public CreatePanelSchedulesCommand(UIApplication uiApp) : base(new CreatePanelSchedulesEventHandler(), uiApp, 90000) { }
    }

    public class CreateWiresCommand : ElectricalCommandBase<WireRequest, WireResult>
    {
        public override string CommandName => "create_wires";
        protected override string InputProperty => null;
        public CreateWiresCommand(UIApplication uiApp) : base(new CreateWiresEventHandler(), uiApp, 90000) { }
    }

    public class CreateAnnotationsCommand : ElectricalCommandBase<AnnotationRequest, AnnotationResult>
    {
        public override string CommandName => "create_annotations";
        protected override string InputProperty => null;
        public CreateAnnotationsCommand(UIApplication uiApp) : base(new CreateAnnotationsEventHandler(), uiApp) { }
    }

    public class CopyLevelElementsCommand : ElectricalCommandBase<CopyLevelRequest, CopyLevelResult>
    {
        public override string CommandName => "copy_level_elements";
        protected override string InputProperty => null;
        public CopyLevelElementsCommand(UIApplication uiApp) : base(new CopyLevelElementsEventHandler(), uiApp, 115000) { }
    }

    public class ExportViewsCommand : ElectricalCommandBase<ExportViewsRequest, ExportViewsResult>
    {
        public override string CommandName => "export_views";
        protected override string InputProperty => null;
        // The MCP client gives up after 120 s, so stop waiting a little earlier.
        public ExportViewsCommand(UIApplication uiApp) : base(new ExportViewsEventHandler(), uiApp, 115000) { }
    }
}
