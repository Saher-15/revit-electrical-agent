using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Documentation;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Documentation
{
    public class PlaceViewSymbolsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private PlaceViewSymbolsEventHandler _handler => (PlaceViewSymbolsEventHandler)Handler;

        public override string CommandName => "place_view_symbols";

        public PlaceViewSymbolsCommand(UIApplication uiApp)
            : base(new PlaceViewSymbolsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                var symbols = parameters?["symbols"]?.ToObject<List<ViewSymbolRequest>>();
                if (symbols == null || symbols.Count == 0)
                    throw new ArgumentException("symbols must hold at least one symbol");
                _handler.Symbols = symbols;
                _handler.ViewId = parameters["viewId"]?.Value<long>() ?? 0;
                if (!RaiseAndWaitForCompletion(60000))
                    throw new TimeoutException("place_view_symbols timed out");
                if (_handler.Error != null)
                    throw new Exception(_handler.Error);
                return new
                {
                    view = _handler.ViewName,
                    placed = _handler.Results.Count(r => r.ElementId > 0),
                    failed = _handler.Results.Count(r => r.ElementId <= 0),
                    results = _handler.Results,
                };
            }
        }
    }
}
