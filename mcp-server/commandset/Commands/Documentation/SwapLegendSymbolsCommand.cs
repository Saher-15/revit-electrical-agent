using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Documentation;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Documentation
{
    public class SwapLegendSymbolsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SwapLegendSymbolsEventHandler _handler => (SwapLegendSymbolsEventHandler)Handler;

        public override string CommandName => "swap_legend_symbols";

        public SwapLegendSymbolsCommand(UIApplication uiApp)
            : base(new SwapLegendSymbolsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                var ids = parameters?["elementIds"]?.ToObject<List<long>>();
                if (ids == null || ids.Count == 0)
                    throw new ArgumentException("elementIds must hold at least one legend component id");
                _handler.ElementIds = ids;
                _handler.ViewId = parameters["viewId"]?.Value<long>() ?? 0;
                _handler.ClockwiseDegrees = parameters["clockwiseDegrees"]?.Value<double>() ?? 90;
                _handler.Apply = parameters["apply"]?.Value<bool>() ?? false;
                _handler.DeleteOriginals = parameters["deleteOriginals"]?.Value<bool>() ?? true;
                // Opening each family for editing takes seconds.
                if (!RaiseAndWaitForCompletion(180000))
                    throw new TimeoutException("swap_legend_symbols timed out");
                if (_handler.Error != null)
                    throw new Exception(_handler.Error);
                return new { applied = _handler.Apply, results = _handler.Results };
            }
        }
    }
}
