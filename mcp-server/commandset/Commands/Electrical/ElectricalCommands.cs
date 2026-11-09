using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Electrical;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Electrical
{
    /// <summary>
    /// Base for the electrical commands: parses the JSON-RPC params into the
    /// handler input, raises the external event and waits for the result.
    /// </summary>
    public abstract class ElectricalCommandBase<TInput, TOutput> : ExternalEventCommandBase
    {
        private readonly int _timeoutMs;

        protected ElectricalHandlerBase<TInput, TOutput> TypedHandler => (ElectricalHandlerBase<TInput, TOutput>)Handler;

        protected ElectricalCommandBase(ElectricalHandlerBase<TInput, TOutput> handler, UIApplication uiApp, int timeoutMs = 60000)
            : base(handler, uiApp)
        {
            _timeoutMs = timeoutMs;
        }

        /// <summary>Which JSON property holds the input ("data" for lists, null for the whole object).</summary>
        protected abstract string InputProperty { get; }

        public override object Execute(JObject parameters, string requestId)
        {
            TInput input;
            try
            {
                JToken token = InputProperty == null ? parameters : parameters?[InputProperty];
                input = token == null ? default : token.ToObject<TInput>();
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"{CommandName}: invalid parameters: {ex.Message}");
            }

            TypedHandler.SetParameters(input);
            if (!RaiseAndWaitForCompletion(_timeoutMs))
                throw new TimeoutException($"{CommandName} timed out after {_timeoutMs / 1000}s. Revit may be showing a dialog or running another command.");
            return TypedHandler.Result;
        }
    }

    public class PlaceElectricalElementCommand : ElectricalCommandBase<List<ElectricalPlacementInfo>, List<PlacedElementInfo>>
    {
        public override string CommandName => "place_electrical_element";
        protected override string InputProperty => "data";
        public PlaceElectricalElementCommand(UIApplication uiApp) : base(new PlaceElectricalElementEventHandler(), uiApp, 115000) { }
    }

    public class CreateCableTrayCommand : ElectricalCommandBase<List<RacewayRunInfo>, List<RacewayRunResult>>
    {
        public override string CommandName => "create_cable_tray";
        protected override string InputProperty => "data";
        public CreateCableTrayCommand(UIApplication uiApp) : base(new CreateRacewayEventHandler(RacewayKind.CableTray), uiApp) { }
    }

    public class CreateConduitCommand : ElectricalCommandBase<List<RacewayRunInfo>, List<RacewayRunResult>>
    {
        public override string CommandName => "create_conduit";
        protected override string InputProperty => "data";
        public CreateConduitCommand(UIApplication uiApp) : base(new CreateRacewayEventHandler(RacewayKind.Conduit), uiApp) { }
    }

    public class CreateElectricalCircuitCommand : ElectricalCommandBase<List<CircuitCreationInfo>, List<CircuitInfo>>
    {
        public override string CommandName => "create_electrical_circuit";
        protected override string InputProperty => "data";
        public CreateElectricalCircuitCommand(UIApplication uiApp) : base(new CreateElectricalCircuitEventHandler(), uiApp) { }
    }

    public class GetElectricalSummaryCommand : ElectricalCommandBase<ElectricalSummaryRequest, ElectricalSummary>
    {
        public override string CommandName => "get_electrical_summary";
        protected override string InputProperty => null;
        public GetElectricalSummaryCommand(UIApplication uiApp) : base(new GetElectricalSummaryEventHandler(), uiApp, 30000) { }
    }

    public class LoadFamilyCommand : ElectricalCommandBase<LoadFamilyInfo, LoadFamilyResult>
    {
        public override string CommandName => "load_family";
        protected override string InputProperty => null;
        public LoadFamilyCommand(UIApplication uiApp) : base(new LoadFamilyEventHandler(), uiApp, 90000) { }
    }
}
