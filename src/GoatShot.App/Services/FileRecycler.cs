using System.Runtime.InteropServices;

namespace GoatShot.App.Services;

/// <summary>
/// Sends library files and receipt folders to the Windows Recycle Bin, so a mistaken delete of
/// evidence can be undone from Explorer. Volumes without a Recycle Bin (some network and
/// removable drives) delete permanently, which is the shell's own behavior.
/// </summary>
public static class FileRecycler
{
    private const uint FileOperationDelete = 0x0003;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoConfirmation = 0x0010;
    private const ushort NoErrorUi = 0x0400;
    private const ushort Silent = 0x0004;

    public static bool TryRecycle(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return true;
        }

        var operation = new ShellFileOperation
        {
            Function = FileOperationDelete,
            // The shell reads a list of paths terminated by an extra null.
            From = Path.GetFullPath(path) + "\0\0",
            Flags = (ushort)(AllowUndo | NoConfirmation | NoErrorUi | Silent)
        };

        var result = SHFileOperation(ref operation);
        if (result != 0)
        {
            error = $"Windows could not move the item to the Recycle Bin (code 0x{result:X}).";
            return false;
        }

        if (operation.AnyOperationsAborted)
        {
            error = "Moving the item to the Recycle Bin was canceled.";
            return false;
        }

        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShellFileOperation operation);
}
