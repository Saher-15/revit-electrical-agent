using Autodesk.Revit.UI;
using RevitMCPCommandSet.Commands.Electrical;
using RevitMCPCommandSet.Models.Production;
using RevitMCPCommandSet.Services.Production;

namespace RevitMCPCommandSet.Commands.Production
{
    // Drawing production: CAD links, PDF markup positions, title blocks and stamps.

    public class ReadCadLinkCommand : ElectricalCommandBase<CadReadRequest, CadReadResult>
    {
        public override string CommandName => "read_cad_link";
        protected override string InputProperty => null;
        public ReadCadLinkCommand(UIApplication uiApp) : base(new ReadCadLinkEventHandler(), uiApp, 115000) { }
    }

    public class SheetPointToModelCommand : ElectricalCommandBase<SheetPointRequest, SheetPointResult>
    {
        public override string CommandName => "sheet_point_to_model";
        protected override string InputProperty => null;
        public SheetPointToModelCommand(UIApplication uiApp) : base(new SheetPointToModelEventHandler(), uiApp) { }
    }

    public class GetTitleBlockFieldsCommand : ElectricalCommandBase<SheetSelection, TitleBlockFieldsResult>
    {
        public override string CommandName => "get_title_block_fields";
        protected override string InputProperty => null;
        public GetTitleBlockFieldsCommand(UIApplication uiApp) : base(new GetTitleBlockFieldsEventHandler(), uiApp) { }
    }

    public class FillTitleBlocksCommand : ElectricalCommandBase<FillTitleBlocksRequest, FillTitleBlocksResult>
    {
        public override string CommandName => "fill_title_blocks";
        protected override string InputProperty => null;
        public FillTitleBlocksCommand(UIApplication uiApp) : base(new FillTitleBlocksEventHandler(), uiApp, 115000) { }
    }

    public class PlaceStampCommand : ElectricalCommandBase<PlaceStampRequest, PlaceStampResult>
    {
        public override string CommandName => "place_stamp";
        protected override string InputProperty => null;
        public PlaceStampCommand(UIApplication uiApp) : base(new PlaceStampEventHandler(), uiApp, 90000) { }
    }
}
