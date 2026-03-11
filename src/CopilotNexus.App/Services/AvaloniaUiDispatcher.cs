namespace CopilotNexus.App.Services;

using Avalonia.Threading;
using CopilotNexus.Core.Interfaces;

public class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);
    public void BeginInvoke(Action action) => Dispatcher.UIThread.Post(action);
}
