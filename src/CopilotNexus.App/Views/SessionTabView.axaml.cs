namespace CopilotNexus.App.Views;

using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CopilotNexus.Core.Models;

public partial class SessionTabView : UserControl
{
    private INotifyCollectionChanged? _messageCollection;

    public SessionTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        InputTextBox.Focus();
        AttachMessageHandlers();
        ScrollToLatestMessage();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        DetachMessageHandlers();
    }

    private void AttachMessageHandlers()
    {
        DetachMessageHandlers();

        if (MessagesList.ItemsSource is not INotifyCollectionChanged collection)
            return;

        _messageCollection = collection;
        _messageCollection.CollectionChanged += OnMessagesCollectionChanged;

        if (MessagesList.ItemsSource is IEnumerable items)
        {
            foreach (var item in items)
            {
                if (item is INotifyPropertyChanged notify)
                    notify.PropertyChanged += OnMessagePropertyChanged;
            }
        }
    }

    private void DetachMessageHandlers()
    {
        if (_messageCollection != null)
        {
            _messageCollection.CollectionChanged -= OnMessagesCollectionChanged;
            _messageCollection = null;
        }

        if (MessagesList.ItemsSource is IEnumerable items)
        {
            foreach (var item in items)
            {
                if (item is INotifyPropertyChanged notify)
                    notify.PropertyChanged -= OnMessagePropertyChanged;
            }
        }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged notify)
                    notify.PropertyChanged += OnMessagePropertyChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged notify)
                    notify.PropertyChanged -= OnMessagePropertyChanged;
            }
        }

        ScrollToLatestMessage();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SessionMessage && (e.PropertyName == nameof(SessionMessage.Content) || e.PropertyName == nameof(SessionMessage.IsStreaming)))
        {
            ScrollToLatestMessage();
        }
    }

    private void ScrollToLatestMessage()
    {
        if (MessagesList.ItemCount <= 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (MessagesList.ItemCount > 0)
                MessagesList.ScrollIntoView(MessagesList.ItemCount - 1);
        }, DispatcherPriority.Background);
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
