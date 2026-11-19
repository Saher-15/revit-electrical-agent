using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Modify
{
    public class SetElementParametersCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetElementParametersEventHandler _handler => (SetElementParametersEventHandler)Handler;

        public override string CommandName => "set_element_parameters";

        public SetElementParametersCommand(UIApplication uiApp)
            : base(new SetElementParametersEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                var changes = parameters?["changes"]?.ToObject<List<ParameterChange>>();
                if (changes == null || changes.Count == 0)
                    throw new ArgumentException("changes must hold at least one element");
                _handler.Changes = changes;
                // ~0.1 s per element; the TS side batches at 50.
                if (!RaiseAndWaitForCompletion(60000))
                    throw new TimeoutException("set_element_parameters timed out");
                if (_handler.Error != null)
                    throw new Exception(_handler.Error);
                return new
                {
                    changed = _handler.Results.Sum(r => r.Changed),
                    failed = _handler.Results.Count(r => r.Errors.Count > 0),
                    results = _handler.Results,
                };
            }
        }
    }
}
