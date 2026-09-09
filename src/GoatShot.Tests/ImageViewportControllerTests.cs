using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Controls;

namespace GoatShot.Tests;

[TestClass]
public sealed class ImageViewportControllerTests
{
    [TestMethod]
    public Task FitKeepsLargeImagesVisibleAndManualZoomExposesScrollbars() => RunSta(() =>
    {
        var (window, scroll, container, _) = CreateViewport();
        try
        {
            var controller = new ImageViewportController(scroll, container);
            window.Show();
            window.UpdateLayout();
            controller.Fit();
            Assert.IsTrue(controller.Scale < 1);
            Assert.IsTrue(scroll.ScrollableWidth < 1 && scroll.ScrollableHeight < 1);
            controller.Zoom(1);
            Assert.IsTrue(scroll.ScrollableWidth > 1000 && scroll.ScrollableHeight > 500);
            scroll.ScrollToHorizontalOffset(500);
            scroll.UpdateLayout();
            Assert.AreEqual(500, scroll.HorizontalOffset, 1);
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task ZoomLeavesPixelSpaceExportAndAnnotationPositionsUnchanged() => RunSta(() =>
    {
        var (window, scroll, container, surface) = CreateViewport();
        try
        {
            var controller = new ImageViewportController(scroll, container);
            window.Show();
            window.UpdateLayout();
            controller.Zoom(1);
            var original = Pixels(surface);
            controller.Zoom(0.25);
            CollectionAssert.AreEqual(original, Pixels(surface));
            controller.Zoom(3);
            scroll.ScrollToHorizontalOffset(420);
            scroll.UpdateLayout();
            CollectionAssert.AreEqual(original, Pixels(surface));
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task ApplyingRelativeCenterPreservesTheViewedRegionAcrossImageSizes() => RunSta(() =>
    {
        var (window, scroll, container, _) = CreateViewport();
        try
        {
            var controller = new ImageViewportController(scroll, container);
            window.Show();
            window.UpdateLayout();
            controller.ApplyView(1.5, new Point(0.7, 0.6));
            Assert.AreEqual(0.7, controller.RelativeCenter.X, 0.002);
            Assert.AreEqual(0.6, controller.RelativeCenter.Y, 0.002);
            container.Width *= 2;
            container.Height *= 2;
            window.UpdateLayout();
            controller.ApplyView(1.5, new Point(0.7, 0.6));
            Assert.AreEqual(0.7, controller.RelativeCenter.X, 0.002);
            Assert.AreEqual(0.6, controller.RelativeCenter.Y, 0.002);
        }
        finally { window.Close(); }
    });

    private static (Window, ScrollViewer, Grid, Canvas) CreateViewport()
    {
        var surface = new Canvas { Width = 1800, Height = 1000, Background = Brushes.White };
        var annotation = new System.Windows.Shapes.Rectangle { Width = 100, Height = 70, Fill = Brushes.Red };
        Canvas.SetLeft(annotation, 850);
        Canvas.SetTop(annotation, 550);
        surface.Children.Add(annotation);
        var container = new Grid { Width = surface.Width, Height = surface.Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        container.Children.Add(surface);
        var scroll = new ScrollViewer { Content = container, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = 600, Height = 400, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Content = scroll };
        return (window, scroll, container, surface);
    }

    private static byte[] Pixels(Canvas surface)
    {
        var bitmap = new RenderTargetBitmap((int)surface.Width, (int)surface.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }

    private static Task RunSta(Action action)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); result.SetResult(); }
            catch (Exception ex) { result.SetException(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
