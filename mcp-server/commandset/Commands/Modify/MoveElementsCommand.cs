using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Modify
{
    public class MoveElementsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private MoveElementsEventHandler _handler => (MoveElementsEventHandler)Handler;

        public override string CommandName => "move_elements";

        public MoveElementsCommand(UIApplication uiApp)
            : base(new MoveElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                var moves = parameters?["moves"]?.ToObject<List<ElementMove>>();
                if (moves == null || moves.Count == 0)
                    throw new ArgumentException("moves must hold at least one element");
                _handler.Moves = moves;
                if (!RaiseAndWaitForCompletion(60000))
                    throw new TimeoutException("move_elements timed out");
                if (_handler.Error != null)
                    throw new Exception(_handler.Error);
                return new
                {
                    moved = _handler.Results.Count(r => r.Moved),
                    failed = _handler.Results.Count(r => !r.Moved),
                    results = _handler.Results,
                };
            }
        }
    }
}
