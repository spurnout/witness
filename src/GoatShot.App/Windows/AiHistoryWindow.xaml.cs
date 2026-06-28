using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.App.Windows;

public partial class AiHistoryWindow : Window
{
    private readonly AppServices _services;
    private readonly ObservableCollection<AiActionHistoryEntry> _entries = new();
    private readonly ObservableCollection<AiPromptHistoryItem> _prompts = new();

    public AiHistoryWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        HistoryList.ItemsSource = _entries;
        PromptHistoryList.ItemsSource = _prompts;
        PromptActionFilter.Items.Add("All actions");
        foreach (var action in Enum.GetNames<AiActionKind>())
        {
            PromptActionFilter.Items.Add(action);
        }

        PromptActionFilter.SelectedIndex = 0;
        RefreshAll();
    }

    private void RefreshAll(string? selectedId = null)
    {
        RefreshHistory(selectedId);
        RefreshPrompts();
        UpdateActionState();
    }

    private void RefreshHistory(string? selectedId = null)
    {
        var entries = _services.AiHistory.Load(100);
        _entries.Clear();
        foreach (var entry in entries)
        {
            _entries.Add(entry);
        }

        HistoryCountText.Text = _entries.Count == 0
            ? "No AI history"
            : $"{_entries.Count} AI histor{(_entries.Count == 1 ? "y entry" : "y entries")}";

        var selected = string.IsNullOrWhiteSpace(selectedId)
            ? _entries.FirstOrDefault()
            : _entries.FirstOrDefault(entry => entry.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        HistoryList.SelectedItem = selected;
        UpdateDetails(selected);
    }

    private void RefreshPrompts()
    {
        var action = SelectedPromptAction();
        var prompts = _services.AiHistory.LoadPromptHistory(action, 50);
        _prompts.Clear();
        foreach (var prompt in prompts)
        {
            _prompts.Add(prompt);
        }
    }

    private AiActionKind? SelectedPromptAction()
    {
        var text = PromptActionFilter.SelectedItem as string;
        return !string.IsNullOrWhiteSpace(text) &&
            Enum.TryParse<AiActionKind>(text, ignoreCase: true, out var action)
                ? action
                : null;
    }

    private AiActionHistoryEntry? SelectedEntry()
    {
        return HistoryList.SelectedItem as AiActionHistoryEntry;
    }

    private AiPromptHistoryItem? SelectedPrompt()
    {
        return PromptHistoryList.SelectedItem as AiPromptHistoryItem;
    }

    private void UpdateDetails(AiActionHistoryEntry? entry)
    {
        if (entry is null)
        {
            EntryTitleText.Text = "No AI action selected";
            EntryMetaText.Text = string.Empty;
            PromptText.Text = string.Empty;
            PreviewText.Text = string.Empty;
            StatusText.Text = "No AI action history yet.";
            UpdateActionState();
            return;
        }

        EntryTitleText.Text = $"{entry.Action} - {entry.FileName}";
        EntryMetaText.Text =
            $"{entry.CreatedAt.LocalDateTime:g} | {(entry.Succeeded ? "Succeeded" : "Failed")} | review={entry.ReviewStatus} | model={entry.ModelId} | id={entry.Id[..Math.Min(10, entry.Id.Length)]}";
        PromptText.Text = entry.Prompt;
        PreviewText.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            new[] { entry.Message, entry.TextPreview, entry.OutputPath }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        StatusText.Text = $"Selected {entry.Action} history entry.";
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        var entry = SelectedEntry();
        AcceptButton.IsEnabled = entry is not null && entry.ReviewStatus != AiActionReviewStatus.Accepted;
        RejectButton.IsEnabled = entry is not null && entry.ReviewStatus != AiActionReviewStatus.Rejected;
        IterateButton.IsEnabled = entry is not null;
        CopyPromptButton.IsEnabled = entry is not null && !string.IsNullOrWhiteSpace(entry.Prompt);
        CopyPreviewButton.IsEnabled = entry is not null && !string.IsNullOrWhiteSpace(PreviewText.Text);
        OpenOutputButton.IsEnabled = entry is not null &&
            !string.IsNullOrWhiteSpace(entry.OutputPath) &&
            File.Exists(entry.OutputPath);
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDetails(SelectedEntry());
    }

    private void PromptActionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PromptHistoryList is not null)
        {
            RefreshPrompts();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAll(SelectedEntry()?.Id);
        StatusText.Text = "AI review history refreshed.";
    }

    private async void Accept_Click(object sender, RoutedEventArgs e)
    {
        await UpdateReviewAsync(AiActionReviewStatus.Accepted, "Accepted in desktop AI review.");
    }

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        await UpdateReviewAsync(AiActionReviewStatus.Rejected, "Rejected in desktop AI review.");
    }

    private async void Iterate_Click(object sender, RoutedEventArgs e)
    {
        var entry = SelectedEntry();
        if (entry is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(entry.Prompt))
        {
            ClipboardInterop.SetText(entry.Prompt);
        }

        await UpdateReviewAsync(AiActionReviewStatus.Iterated, "Marked for iteration; prompt copied for reuse.");
    }

    private async Task UpdateReviewAsync(AiActionReviewStatus status, string note)
    {
        var entry = SelectedEntry();
        if (entry is null)
        {
            return;
        }

        var updated = await _services.AiHistory.UpdateReviewStatusAsync(entry.Id, status, note);
        if (updated is null)
        {
            StatusText.Text = "AI history entry was not found.";
            return;
        }

        RefreshAll(updated.Id);
        StatusText.Text = $"{updated.Action} marked {updated.ReviewStatus}.";
    }

    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        var entry = SelectedEntry();
        if (entry is null || string.IsNullOrWhiteSpace(entry.Prompt))
        {
            return;
        }

        ClipboardInterop.SetText(entry.Prompt);
        StatusText.Text = "Copied selected AI prompt.";
    }

    private void CopyPreview_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PreviewText.Text))
        {
            return;
        }

        ClipboardInterop.SetText(PreviewText.Text);
        StatusText.Text = "Copied selected AI preview.";
    }

    private void CopySelectedPrompt_Click(object sender, RoutedEventArgs e)
    {
        var prompt = SelectedPrompt();
        if (prompt is null || string.IsNullOrWhiteSpace(prompt.Prompt))
        {
            return;
        }

        ClipboardInterop.SetText(prompt.Prompt);
        StatusText.Text = "Copied prompt-history item.";
    }

    private async void UsePromptForIteration_Click(object sender, RoutedEventArgs e)
    {
        var prompt = SelectedPrompt();
        if (prompt is null || string.IsNullOrWhiteSpace(prompt.Prompt))
        {
            StatusText.Text = "Select a prompt-history item first.";
            return;
        }

        ClipboardInterop.SetText(prompt.Prompt);
        var entry = SelectedEntry();
        if (entry is not null)
        {
            var updated = await _services.AiHistory.UpdateReviewStatusAsync(
                entry.Id,
                AiActionReviewStatus.Iterated,
                $"Prompt-history item reused for {prompt.Action} iteration.");
            RefreshAll(updated?.Id ?? entry.Id);
        }

        StatusText.Text = "Copied prompt-history item for iteration.";
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        var entry = SelectedEntry();
        if (entry is null || string.IsNullOrWhiteSpace(entry.OutputPath) || !File.Exists(entry.OutputPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(entry.OutputPath) { UseShellExecute = true });
            StatusText.Text = "Opened AI output.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open AI output: {ex.Message}";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
