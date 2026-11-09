using Autodesk.Revit.UI;
using RevitMCPCommandSet.Commands.Electrical;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Services.Families;

namespace RevitMCPCommandSet.Commands.Families
{
    // Family creation and editing. They reuse the silent electrical command plumbing.

    public class CreateFamilyCommand : ElectricalCommandBase<CreateFamilyRequest, FamilyResult>
    {
        public override string CommandName => "create_family";
        protected override string InputProperty => null;
        // The MCP client gives up after 120 s, so stop waiting a little earlier.
        public CreateFamilyCommand(UIApplication uiApp) : base(new CreateFamilyEventHandler(), uiApp, 115000) { }
    }

    public class EditFamilyCommand : ElectricalCommandBase<EditFamilyRequest, FamilyResult>
    {
        public override string CommandName => "edit_family";
        protected override string InputProperty => null;
        public EditFamilyCommand(UIApplication uiApp) : base(new EditFamilyEventHandler(), uiApp, 115000) { }
    }

    public class SetFamilyTypeCommand : ElectricalCommandBase<List<FamilyTypeRequest>, List<FamilyTypeResult>>
    {
        public override string CommandName => "set_family_type";
        protected override string InputProperty => "data";
        public SetFamilyTypeCommand(UIApplication uiApp) : base(new SetFamilyTypeEventHandler(), uiApp) { }
    }
}
