using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Electrical
{
    /// <summary>
    /// Shared plumbing for the electrical command handlers.
    /// Never shows a TaskDialog: every failure is returned to the client as
    /// an AIResult, because a modal dialog inside Revit blocks the whole
    /// request queue of the MCP bridge.
    /// </summary>
    public abstract class ElectricalHandlerBase<TInput, TOutput> : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        protected UIApplication UiApp { get; private set; }
        protected Document Doc => UiApp.ActiveUIDocument.Document;

        public TInput Input { get; private set; }
        public AIResult<TOutput> Result { get; private set; }
        protected List<string> Warnings { get; } = new List<string>();

        public void SetParameters(TInput input)
        {
            Input = input;
            Warnings.Clear();
            Result = null;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            UiApp = app;
            try
            {
                if (app.ActiveUIDocument == null)
                {
                    Result = Fail("No document is open in Revit.");
                    return;
                }
                Result = Run();
            }
            catch (Exception ex)
            {
                Result = Fail($"{GetName()} failed: {ex.Message}");
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        /// <summary>Does the work on the Revit API thread and returns the result.</summary>
        protected abstract AIResult<TOutput> Run();

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public abstract string GetName();

        protected AIResult<TOutput> Ok(TOutput response, string message)
        {
            if (Warnings.Count > 0)
                message += "\n\nWarnings:\n  - " + string.Join("\n  - ", Warnings);
            return new AIResult<TOutput> { Success = true, Message = message, Response = response };
        }

        protected AIResult<TOutput> Fail(string message)
        {
            if (Warnings.Count > 0)
                message += "\n\nWarnings:\n  - " + string.Join("\n  - ", Warnings);
            return new AIResult<TOutput> { Success = false, Message = message };
        }
    }
}
