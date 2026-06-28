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
        HistoryList.ItemsSource = _historyItems;
        QueueList.ItemsSource = _queueItems;
        RefreshAll();
    }

    public bool QueueChanged { get; private set; }

    private void RefreshAll()
    {
        RefreshHistory();
        RefreshQueue();
        UpdateActionState();
    }

    private void RefreshHistory()
    {
        var entries = _services.Sharing.SearchHistory(
            query: SearchBox.Text,
            limit: 100);

        _historyItems.Clear();
        foreach (var model in ShareHistoryActions.BuildModels(entries))
        {
            _historyItems.Add(model);
        }

        HistoryCountText.Text = _historyItems.Count == 0
            ? "No matching share history"
            : $"{_historyItems.Count} share histor{(_historyItems.Count == 1 ? "y item" : "y items")}";
    }

    private void RefreshQueue()
    {
        var items = _services.UploadQueue
            .ListAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

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
        RefreshQueue();
        StatusText.Text = $"Queued retry {queued.ShortId} for {selected.FileName}.";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAll();
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
        RefreshQueue();
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
        RefreshQueue();
        StatusText.Text = updated is null
            ? $"Queue item not found: {selected.ShortId}."
            : $"{updated.ShortId}: {updated.LastMessage}";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
