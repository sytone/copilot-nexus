namespace CopilotFamily.Core.Interfaces;

/// <summary>
/// Abstracts UI thread dispatching so ViewModels can be tested
/// without a real WPF Dispatcher.
/// </summary>
public interface IUiDispatcher
{
    void Invoke(Action action);
    void BeginInvoke(Action action);
}
