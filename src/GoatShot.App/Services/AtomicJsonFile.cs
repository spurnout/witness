using System.Text.Json;

namespace GoatShot.App.Services;

internal static class AtomicJsonFile
{
    /// <summary>
    /// Replaces <paramref name="path"/> with a complete document. <paramref name="flushToDisk"/>
    /// can be turned off for rebuildable caches, where losing the newest write on power loss is
    /// acceptable and an fsync per file would dominate bulk writes.
    /// </summary>
    public static void Write<T>(string path, T value, JsonSerializerOptions options, bool flushToDisk = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, options);
                stream.Flush(flushToDisk);
            }

            // Publish only a complete document; failed serialization or writes leave the old file intact.
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
