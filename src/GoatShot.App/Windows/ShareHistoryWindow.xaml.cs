using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.App.Windows;

public partial class ShareHistoryWindow : Window
{
    private readonly AppServices _services;
    private readonly ObservableCollection<ShareHistoryActionModel> _historyItems = new();
    private readonly ObservableCollection<UploadQueueItem> _queueItems = new();

    public ShareHistoryWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        EscapeKeyCloseBehavior.Attach(this);
        HistoryList.ItemsSource = _historyItems;
        QueueList.ItemsSource = _queueItems;
        RefreshHistory();
        UpdateActionState();
        Loaded += async (_, _) => await RefreshQueueAsync();
    }

    public bool QueueChanged { get; private set; }

    private async Task RefreshAllAsync()
    {
        RefreshHistory();
        await RefreshQueueAsync();
        UpdateActionState();
    }

    private void RefreshHistory()
    {
        var entries = _services.Sharing.SearchHistory(
            query: SearchBox.Text,
            limit: 100);

        var models = ShareHistoryActions.BuildModels(entries)
            .Where(MatchesStatusFilter)
            .Where(MatchesDestinationFilter);

        _historyItems.Clear();
        foreach (var model in models)
        {
            _historyItems.Add(model);
        }

        HistoryCountText.Text = _historyItems.Count == 0
            ? "No matching share history"
            : $"{_historyItems.Count} share histor{(_historyItems.Count == 1 ? "y item" : "y items")}";
    }

    private bool MatchesStatusFilter(ShareHistoryActionModel model)
    {
        var value = (StatusFilterBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return value switch
        {
            "Succeeded" => model.Entry.Succeeded,
            "Failed" => !model.Entry.Succeeded,
            _ => true
        };
    }

    private bool MatchesDestinationFilter(ShareHistoryActionModel model)
    {
        var value = (DestinationFilterBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return value switch
        {
            "External only" => model.Entry.ExternalDestination,
            "Local only" => !model.Entry.ExternalDestination,
            null or "All destinations" => true,
            _ => model.Entry.Destination.ToString().Equals(value, StringComparison.OrdinalIgnoreCase)
        };
    }

    private async Task RefreshQueueAsync()
    {
        var items = await _services.UploadQueue.ListAsync(CancellationToken.None);

        _queueItems.Clear();
        foreach (var item in items.Take(25))
        {
            _queueItems.Add(item);
        }

        var diagnostics = _services.UploadQueue.GetDiagnostics();
        QueueSummaryText.Text = diagnostics.Summary;
    }

    private ShareHistoryActionModel? SelectedHistory()
    {
        return HistoryList.SelectedItem as ShareHistoryActionModel;
    }

    private UploadQueueItem? SelectedQueue()
    {
        return QueueList.SelectedItem as UploadQueueItem;
    }

    private void UpdateActionState()
    {
        var history = SelectedHistory();
        CopyUrlButton.IsEnabled = history?.CanCopyUrl == true;
        OpenUrlButton.IsEnabled = history?.CanOpenUrl == true;
        CopyMarkdownButton.IsEnabled = history?.CanCopyMarkdown == true;
        RetryHistoryButton.IsEnabled = history?.CanRetry == true;

        var queue = SelectedQueue();
        CancelQueueButton.IsEnabled = queue?.CanCancel == true;
        RetryQueueButton.IsEnabled = queue?.CanRetry == true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (HistoryList is null)
        {
            return;
        }

        RefreshHistory();
        UpdateActionState();
    }

    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList is null)
        {
            return;
        }

        RefreshHistory();
        UpdateActionState();
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SelectedHistory();
        StatusText.Text = selected is null
            ? string.Empty
            : $"{selected.ShortId}: {selected.StatusText} via {selected.DestinationText}.";
        UpdateActionState();
    }

    private void QueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SelectedQueue();
        StatusText.Text = selected is null
            ? string.Empty
            : $"{selected.ShortId}: {selected.DetailLabel}";
        UpdateActionState();
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedHistory();
        if (selected?.CanCopyUrl == true)
        {
            ShareHistoryActions.CopyUrl(selected.Entry);
            StatusText.Text = $"Copied URL for {selected.ShortId}.";
        }
    }

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedHistory();
        if (selected?.CanOpenUrl == true)
        {
            ShareHistoryActions.OpenUrl(selected.Entry);
            StatusText.Text = $"Opened URL for {selected.ShortId}.";
        }
    }

    private void CopyMarkdown_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedHistory();
        if (selected?.CanCopyMarkdown == true)
        {
            ShareHistoryActions.CopyMarkdown(selected.Entry);
            StatusText.Text = $"Copied Markdown for {selected.ShortId}.";
        }
    }

    private async void RetryHistory_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedHistory();
        if (selected?.CanRetry != true)
        {
            return;
        }

        var queued = await _services.UploadQueue.EnqueueAsync(
            ShareHistoryActions.ToRetryCaptureItem(selected.Entry),
            selected.Entry.Destination,
            CancellationToken.None);
        QueueChanged = true;
        await RefreshQueueAsync();
        StatusText.Text = $"Queued retry {queued.ShortId} for {selected.FileName}.";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
        StatusText.Text = "Share history and upload queue refreshed.";
    }

    private async void CancelQueue_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedQueue();
        if (selected is null)
        {
            return;
        }

        var updated = await _services.UploadQueue.CancelAsync(selected.Id, CancellationToken.None);
        QueueChanged = true;
        await RefreshQueueAsync();
        StatusText.Text = updated is null
            ? $"Queue item not found: {selected.ShortId}."
            : $"{updated.ShortId}: {updated.LastMessage}";
    }

    private async void RetryQueue_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedQueue();
        if (selected is null)
        {
            return;
        }

        var updated = await _services.UploadQueue.RetryAsync(selected.Id, CancellationToken.None);
        QueueChanged = true;
        await RefreshQueueAsync();
        StatusText.Text = updated is null
            ? $"Queue item not found: {selected.ShortId}."
            : $"{updated.ShortId}: {updated.LastMessage}";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
