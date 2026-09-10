using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Services;
using GoatShot.App.Windows;

namespace GoatShot.Tests;

[TestClass]
public sealed class CaptureGalleryTests
{
    [TestMethod]
    public void HistoryIncludesAllImagesNewestFirstWithoutDuplicatesOrPrivateHistory()
    {
        var captures = Enumerable.Range(0, 2000).Select(CreateCapture).ToList();
        var latest = captures[123]; // Capture order must win even if timestamps are out of order.
        captures.Add(new CaptureItem { FilePath = "recording.mp4", Kind = CaptureKind.RecordingMp4 });
        captures.Add(new CaptureItem { FilePath = "private.png", IsPrivate = true });
        var entries = CaptureGalleryModels.BuildItems(captures, latest);
        Assert.AreEqual(2000, entries.Count);
        Assert.AreSame(latest, entries[0].Item);
        Assert.AreEqual("capture-1999", entries[1].Item.Id);
        Assert.AreEqual(1, entries.Count(entry => entry.IsSelected));
        Assert.IsFalse(entries.Any(entry => entry.Item.IsPrivate));
    }

    [TestMethod]
    public void PrivateCaptureShowsOnlyTheCurrentTemporaryImage()
    {
        var latest = CreateCapture(3);
        latest.IsPrivate = true;
        var entries = CaptureGalleryModels.BuildItems([CreateCapture(0), CreateCapture(1)], latest);
        Assert.AreEqual(1, entries.Count);
        Assert.AreSame(latest, entries[0].Item);
    }

    [TestMethod]
    public void GalleryRenderArgumentDoesNotOpenTheActionsRenderer()
    {
        var options = AppStartupOptions.Parse(["--render-capture-gallery-output", "gallery.png"]);
        Assert.IsTrue(options.RenderCaptureGallery);
        Assert.AreEqual("gallery.png", options.RenderCaptureGalleryOutputPath);
        Assert.IsFalse(options.RenderCaptureTask);
    }

    [TestMethod]
    public Task GalleryVirtualizesHistoryAndWaitsForDoubleClickToRequestActions() => RunSta(() =>
    {
        var captures = Enumerable.Range(0, 2000).Select(CreateCapture).ToArray();
        var window = CreateWindow(0, captures);
        try
        {
            CaptureItem? requested = null;
            window.CaptureRequested += (_, item) => requested = item;
            var rows = (ListBox)window.FindName("CaptureRows");
            Assert.IsTrue(rows.Items.Count > 100);
            Assert.IsNull(rows.ItemContainerGenerator.ContainerFromIndex(rows.Items.Count - 1), "Off-screen history should not create thumbnails.");
            Assert.IsFalse(window.ShowActivated, "Capture feedback must not steal focus.");
            var buttons = Descendants<Button>(window).ToArray();
            Assert.IsTrue(buttons.Length < 100);
            Assert.IsFalse(buttons.Any(button => button.Content is string text && text is "Edit" or "Share" or "Copy image"));
            var button = buttons.First(candidate => candidate.DataContext is CaptureGalleryEntry entry && !entry.IsSelected);
            var selected = (CaptureGalleryEntry)button.DataContext;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(window.IsVisible);
            Assert.IsTrue(selected.IsSelected);
            Assert.IsNull(requested, "Selecting a thumbnail must leave image controls hidden.");
            window.Width = 700;
            window.UpdateLayout();
            Assert.IsTrue(selected.IsSelected, "Reflow must preserve the selected image.");
            button = Descendants<Button>(window).First(candidate => ReferenceEquals(candidate.DataContext, selected));
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent
            });
            Assert.AreSame(selected.Item, requested);
            Assert.IsFalse(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task HoverRestoresAnInProgressFadeAndLeavingRestartsDismissal() => RunSta(() =>
    {
        var window = CreateWindow(1, [CreateCapture(0)]);
        try
        {
            PumpUntil(() => window.Opacity < 0.95, TimeSpan.FromSeconds(3));
            window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
            Pump(TimeSpan.FromMilliseconds(1800));
            Assert.IsTrue(window.IsVisible, "Hover must cancel the pending fade completion.");
            Assert.AreEqual(1d, window.Opacity, 0.001);
            window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.IsTrue(window.IsVisible, "Leaving should grant a fresh full delay.");
            PumpUntil(() => !window.IsVisible, TimeSpan.FromSeconds(3));
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task NewCaptureCancelsTheOldFadeAndRefreshesTheExistingGallery() => RunSta(() =>
    {
        var previous = CreateCapture(0);
        var latest = CreateCapture(1);
        var window = CreateWindow(1, [previous]);
        try
        {
            PumpUntil(() => window.Opacity < 0.95, TimeSpan.FromSeconds(3));
            window.UpdateCaptures([previous, latest], latest);
            Assert.AreEqual(1d, window.Opacity, 0.001);
            Pump(TimeSpan.FromMilliseconds(700));
            Assert.IsTrue(window.IsVisible, "The old fade must not dismiss the new capture.");
            var firstRow = (CaptureGalleryEntry[])((ListBox)window.FindName("CaptureRows")).Items[0];
            Assert.AreSame(latest, firstRow[0].Item);
            PumpUntil(() => !window.IsVisible, TimeSpan.FromSeconds(3));
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task ZeroDelayKeepsTheGalleryOpenAndMissingThumbnailsAreHarmless() => RunSta(() =>
    {
        var item = CreateCapture(0);
        var converter = new CaptureGalleryThumbnailConverter();
        Assert.IsNull(converter.Convert(item, typeof(object), null!, CultureInfo.InvariantCulture));
        var window = CreateWindow(0, [item]);
        try
        {
            Pump(TimeSpan.FromMilliseconds(1800));
            Assert.IsTrue(window.IsVisible);
            Assert.AreEqual(1d, window.Opacity, 0.001);
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task CorruptThumbnailFallsBackToTheImageWithoutAnErrorOverlayOrFileLock() => RunSta(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), $"receipts-gallery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        CaptureGalleryWindow? window = null;
        try
        {
            var item = CreateCapture(0);
            item.FilePath = Path.Combine(root, "image.png");
            item.ThumbnailPath = Path.Combine(root, "broken.png");
            File.WriteAllText(item.ThumbnailPath, "not an image");
            var bitmap = BitmapSource.Create(80, 160, 96, 96, PixelFormats.Bgr32, null, new byte[80 * 160 * 4], 80 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(item.FilePath)) encoder.Save(stream);

            window = CreateWindow(0, [item]);
            Pump(TimeSpan.FromMilliseconds(100));
            var image = Descendants<Image>(window).Single();
            Assert.IsNotNull(image.Source, "A corrupt thumbnail should fall back to the saved image.");
            Assert.IsTrue(Descendants<TextBlock>(window).Where(text => text.Text == "Preview unavailable")
                .All(text => text.Visibility == Visibility.Collapsed), "Valid portrait previews must not show error text around their edges.");
            File.Delete(item.FilePath);
            Assert.IsFalse(File.Exists(item.FilePath), "Thumbnails must not hold capture files open.");
        }
        finally
        {
            window?.Close();
            File.Delete(Path.Combine(root, "image.png"));
            File.Delete(Path.Combine(root, "broken.png"));
            Directory.Delete(root);
        }
    });

    private static CaptureItem CreateCapture(int index) => new()
    {
        Id = $"capture-{index}", FilePath = Path.Combine(Path.GetTempPath(), "receipts-gallery-missing", $"capture-{index}.png"),
        Kind = CaptureKind.Region, Width = 800, Height = 600,
        CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(index)
    };

    private static CaptureGalleryWindow CreateWindow(int seconds, CaptureItem[] captures)
    {
        var window = new CaptureGalleryWindow(seconds) { Left = -10000, Top = -10000, Topmost = false };
        window.UpdateCaptures(captures, captures[^1]);
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < timeout) Pump(TimeSpan.FromMilliseconds(25));
        Assert.IsTrue(condition(), "The expected gallery state did not arrive before the timeout.");
    }

    private static Task RunSta(Action action)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); result.SetResult(); }
            catch (Exception exception) { result.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
