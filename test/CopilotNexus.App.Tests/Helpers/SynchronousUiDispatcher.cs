namespace CopilotNexus.App.Tests.Helpers;

using CopilotNexus.Core.Interfaces;

/// <summary>
/// Synchronous dispatcher for unit tests — executes actions immediately
/// on the calling thread instead of marshalling to a UI thread.
/// </summary>
public class SynchronousUiDispatcher : IUiDispatcher
{
    public void Invoke(Action action) => action();
    public void BeginInvoke(Action action) => action();
}
