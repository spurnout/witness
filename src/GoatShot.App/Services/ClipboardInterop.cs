using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace GoatShot.App.Services;

public static class ClipboardInterop
{
    private const int ClipboardRetryCount = 10;
    private const int ClipboardRetryDelayMilliseconds = 25;
    private const int ClipboardCantOpenHResult = unchecked((int)0x800401D0);
    private static readonly object ClipboardWriteLock = new();

    public static BitmapSource? GetImage()
    {
        return RunSta<BitmapSource?>(() =>
        {
            if (!System.Windows.Clipboard.ContainsImage())
            {
                return null;
            }

            var image = System.Windows.Clipboard.GetImage();
            image?.Freeze();
            return image;
        });
    }

    public static IReadOnlyList<string> GetFileDropList()
    {
        return RunSta<IReadOnlyList<string>>(() =>
        {
            if (!System.Windows.Clipboard.ContainsFileDropList())
            {
                return Array.Empty<string>();
            }

            return System.Windows.Clipboard.GetFileDropList()
                .Cast<string>()
                .ToList();
        });
    }

    public static void SetText(string text)
    {
        RunClipboardWrite(() => System.Windows.Clipboard.SetText(text));
    }

    public static void SetImage(BitmapSource image)
    {
        RunClipboardWrite(() => System.Windows.Clipboard.SetImage(image));
    }

    public static void SetFileDropList(IEnumerable<string> paths)
    {
        RunClipboardWrite(() =>
        {
            var files = new StringCollection();
            foreach (var path in paths)
            {
                files.Add(path);
            }

            System.Windows.Clipboard.SetFileDropList(files);
        });
    }

    private static void RunClipboardWrite(Action action)
    {
        lock (ClipboardWriteLock)
        {
            for (var attempt = 1; attempt <= ClipboardRetryCount; attempt++)
            {
                try
                {
                    RunSta(action);
                    return;
                }
                catch (COMException ex) when (IsClipboardBusy(ex) && attempt < ClipboardRetryCount)
                {
                    Thread.Sleep(ClipboardRetryDelayMilliseconds * attempt);
                }
            }
        }
    }

    private static bool IsClipboardBusy(COMException exception)
    {
        return exception.HResult == ClipboardCantOpenHResult;
    }

    private static void RunSta(Action action)
    {
        RunSta<object?>(() =>
        {
            action();
            return null;
        });
    }

    private static T RunSta<T>(Func<T> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return action();
        }

        Exception? exception = null;
        T? result = default;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }

        return result!;
    }
}
