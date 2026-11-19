using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Modify
{
    public class RotateElementsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private RotateElementsEventHandler _handler => (RotateElementsEventHandler)Handler;

        public override string CommandName => "rotate_elements";

        public RotateElementsCommand(UIApplication uiApp)
            : base(new RotateElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                var ids = parameters?["elementIds"]?.ToObject<List<long>>();
                if (ids == null || ids.Count == 0)
                    throw new ArgumentException("elementIds must hold at least one id");
                _handler.ElementIds = ids;
                _handler.ClockwiseDegrees = parameters["clockwiseDegrees"]?.Value<double>() ?? 90;
                _handler.CommonCenter = parameters["commonCenter"]?.Value<bool>() ?? false;
                if (!RaiseAndWaitForCompletion(60000))
                    throw new TimeoutException("rotate_elements timed out");
                if (_handler.Error != null)
                    throw new Exception(_handler.Error);
                return new
                {
                    rotated = _handler.Results.Count(r => r.Rotated),
                    failed = _handler.Results.Count(r => !r.Rotated),
                    results = _handler.Results,
                };
            }
        }
    }
}
