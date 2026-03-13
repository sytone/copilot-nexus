namespace CopilotNexus.App.Views;

using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CopilotNexus.Core.Models;

public partial class SessionTabView : UserControl
{
    private const double ScrollBottomTolerance = 8.0;
    private static readonly TimeSpan MinimumResumeDelay = TimeSpan.FromMilliseconds(50);
    private INotifyCollectionChanged? _messageCollection;
    private INotifyPropertyChanged? _viewModel;
    private ScrollViewer? _messagesScrollViewer;
    private DispatcherTimer? _autoScrollResumeTimer;
    private DateTimeOffset _lastUserScrollInteractionUtc;
    private double _lastObservedOffsetY = double.NaN;
    private bool _autoScrollPausedByUser;
    private bool _suppressScrollEventHandling;

    internal static TimeSpan AutoScrollResumeDelay { get; set; } = TimeSpan.FromMinutes(1);

    public SessionTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        InputTextBox.Focus();
        AttachViewModelHandlers();
        AttachMessageHandlers();
        AttachScrollHandlers();
        ScrollToLatestMessage(force: true);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        DetachMessageHandlers();
        DetachViewModelHandlers();
        DetachScrollHandlers();
        StopAutoScrollResumeTimer();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachViewModelHandlers();
        UpdateAutoScrollResumeTimer();
    }

    private void AttachViewModelHandlers()
    {
        DetachViewModelHandlers();

        if (DataContext is not INotifyPropertyChanged notify)
            return;

        _viewModel = notify;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachViewModelHandlers()
    {
        if (_viewModel == null)
            return;

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ViewModels.SessionTabViewModel.IsProcessing), StringComparison.Ordinal))
            return;

        UpdateAutoScrollResumeTimer();
    }

    private void AttachScrollHandlers()
    {
        DetachScrollHandlers();

        _messagesScrollViewer = MessagesList.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

        if (_messagesScrollViewer == null)
            return;

        _lastObservedOffsetY = _messagesScrollViewer.Offset.Y;
        _messagesScrollViewer.ScrollChanged += OnMessagesScrollChanged;
    }

    private void DetachScrollHandlers()
    {
        if (_messagesScrollViewer != null)
        {
            _messagesScrollViewer.ScrollChanged -= OnMessagesScrollChanged;
            _messagesScrollViewer = null;
        }

        _lastObservedOffsetY = double.NaN;
    }

    private void OnMessagesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_messagesScrollViewer == null)
            return;

        var currentOffsetY = _messagesScrollViewer.Offset.Y;
        if (_suppressScrollEventHandling)
        {
            _lastObservedOffsetY = currentOffsetY;
            return;
        }

        var userMovedScrollPosition = double.IsNaN(_lastObservedOffsetY) ||
            Math.Abs(currentOffsetY - _lastObservedOffsetY) > 0.5;
        _lastObservedOffsetY = currentOffsetY;

        if (!userMovedScrollPosition || !IsSessionActive())
            return;

        if (IsScrolledToBottom())
        {
            _autoScrollPausedByUser = false;
            StopAutoScrollResumeTimer();
            return;
        }

        _autoScrollPausedByUser = true;
        _lastUserScrollInteractionUtc = DateTimeOffset.UtcNow;
        ScheduleAutoScrollResume();
    }

    private void UpdateAutoScrollResumeTimer()
    {
        if (!_autoScrollPausedByUser)
        {
            StopAutoScrollResumeTimer();
            return;
        }

        if (!IsSessionActive())
        {
            StopAutoScrollResumeTimer();
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _lastUserScrollInteractionUtc;
        if (elapsed >= AutoScrollResumeDelay)
        {
            ResumeAutoScrollAndScrollToLatest();
            return;
        }

        ScheduleAutoScrollResume(AutoScrollResumeDelay - elapsed);
    }

    private void ScheduleAutoScrollResume(TimeSpan? delay = null)
    {
        var interval = delay ?? AutoScrollResumeDelay;
        if (interval < MinimumResumeDelay)
            interval = MinimumResumeDelay;

        _autoScrollResumeTimer ??= new DispatcherTimer();
        _autoScrollResumeTimer.Tick -= OnAutoScrollResumeTimerTick;
        _autoScrollResumeTimer.Tick += OnAutoScrollResumeTimerTick;
        _autoScrollResumeTimer.Stop();
        _autoScrollResumeTimer.Interval = interval;
        _autoScrollResumeTimer.Start();
    }

    private void StopAutoScrollResumeTimer()
    {
        _autoScrollResumeTimer?.Stop();
    }

    private void OnAutoScrollResumeTimerTick(object? sender, EventArgs e)
    {
        if (!_autoScrollPausedByUser || !IsSessionActive())
        {
            StopAutoScrollResumeTimer();
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _lastUserScrollInteractionUtc;
        if (elapsed < AutoScrollResumeDelay)
        {
            ScheduleAutoScrollResume(AutoScrollResumeDelay - elapsed);
            return;
        }

        ResumeAutoScrollAndScrollToLatest();
    }

    private void ResumeAutoScrollAndScrollToLatest()
    {
        _autoScrollPausedByUser = false;
        StopAutoScrollResumeTimer();
        ScrollToLatestMessage(force: true);
    }

    private bool IsSessionActive() =>
        DataContext is ViewModels.SessionTabViewModel vm && vm.IsProcessing;

    private bool IsScrolledToBottom()
    {
        if (_messagesScrollViewer == null)
            return true;

        var maxOffsetY = Math.Max(0, _messagesScrollViewer.Extent.Height - _messagesScrollViewer.Viewport.Height);
        if (maxOffsetY <= 0)
            return true;

        return maxOffsetY - _messagesScrollViewer.Offset.Y <= ScrollBottomTolerance;
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

        TryAutoScrollToLatestMessage();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SessionMessage && (e.PropertyName == nameof(SessionMessage.Content) || e.PropertyName == nameof(SessionMessage.IsStreaming)))
        {
            TryAutoScrollToLatestMessage();
        }
    }

    private void TryAutoScrollToLatestMessage()
    {
        if (_autoScrollPausedByUser)
            return;

        ScrollToLatestMessage();
    }

    private void ScrollToLatestMessage(bool force = false)
    {
        if (MessagesList.ItemCount <= 0)
            return;

        if (!force && _autoScrollPausedByUser)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (MessagesList.ItemCount <= 0)
                return;

            _suppressScrollEventHandling = true;
            MessagesList.ScrollIntoView(MessagesList.ItemCount - 1);
            Dispatcher.UIThread.Post(() =>
            {
                if (_messagesScrollViewer != null)
                    _lastObservedOffsetY = _messagesScrollViewer.Offset.Y;
                _suppressScrollEventHandling = false;
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }

    private void InputTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.SessionTabViewModel vm)
            return;

        if (e.Key == Key.Up)
        {
            if (vm.TryNavigateInputHistory(-1))
                e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (vm.TryNavigateInputHistory(1))
                e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private async void CopyMessageMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
            return;

        var message = menuItem.Tag as SessionMessage ?? menuItem.DataContext as SessionMessage;
        if (message == null || string.IsNullOrWhiteSpace(message.Content))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null)
            return;

        await topLevel.Clipboard.SetTextAsync(message.Content);
    }
}
