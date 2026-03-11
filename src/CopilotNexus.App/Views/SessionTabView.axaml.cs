namespace CopilotNexus.App.Views;

using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

public partial class SessionTabView : UserControl
{
    public SessionTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        InputTextBox.Focus();

        if (MessagesList.ItemsSource is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += (_, _) =>
            {
                if (MessagesList.ItemCount > 0)
                {
                    MessagesList.ScrollIntoView(MessagesList.ItemCount - 1);
                }
            };
        }
    }

    private void InputTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ViewModels.SessionTabViewModel vm)
        {
            if (vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
            }

            e.Handled = true;
        }
    }
}
