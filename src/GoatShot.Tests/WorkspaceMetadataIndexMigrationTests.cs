using GoatShot.App.Models;
using GoatShot.App.Services;
using Microsoft.Data.Sqlite;

namespace GoatShot.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WorkspaceMetadataIndexMigrationTests
{
    [TestMethod]
    public void EnsureCreated_RecreatesFtsWhenSourceUrlColumnMissing()
    {
        WithTempPaths(paths =>
        {
            CreateLegacyDatabase(paths.MetadataDatabasePath);
            var index = new WorkspaceMetadataIndex(paths);

            index.EnsureCreated();

            using var connection = Open(paths.MetadataDatabasePath);
            var ftsColumns = ReadColumns(connection, "captures_fts");
            CollectionAssert.Contains(ftsColumns, "source_window_title");
            CollectionAssert.Contains(ftsColumns, "source_url");
            // The sentinel swap drops the old table, so the stale row must be gone.
            Assert.AreEqual(0L, Scalar(connection, "SELECT COUNT(*) FROM captures_fts;"));
        });
    }

    [TestMethod]
    public void Rebuild_RepopulatesSearchAfterFtsMigration()
    {
        WithTempPaths(paths =>
        {
            CreateLegacyDatabase(paths.MetadataDatabasePath);
            var index = new WorkspaceMetadataIndex(paths);
            var item = new CaptureItem
            {
                Kind = CaptureKind.Region,
                CreatedAt = DateTimeOffset.Now,
                FilePath = @"C:\captures\upgrade.png",
                ThumbnailPath = @"C:\captures\upgrade.thumb.png",
                SourceWindowTitle = "Checkout - Chrome",
                SourceUrl = "https://example.test/invoices"
            };

            // Startup calls Rebuild, whose own EnsureCreated performs the drop/recreate; the
            // repopulation must land in the same pass or search silently goes empty after upgrade.
            index.Rebuild([item]);

            Assert.IsTrue(index.SearchIds("invoices").Contains(item.Id, StringComparer.OrdinalIgnoreCase));
            Assert.IsTrue(index.SearchIds("checkout").Contains(item.Id, StringComparer.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public void Upsert_RoundTripsSourceWindowTitleAndUrlColumns()
    {
        WithTempPaths(paths =>
        {
            var index = new WorkspaceMetadataIndex(paths);
            var item = new CaptureItem
            {
                Kind = CaptureKind.ActiveWindow,
                CreatedAt = DateTimeOffset.Now,
                FilePath = @"C:\captures\window.png",
                ThumbnailPath = @"C:\captures\window.thumb.png",
                SourceWindowTitle = "Invoice editor",
                SourceUrl = "https://example.test/edit"
            };

            index.Upsert(item);

            using var connection = Open(paths.MetadataDatabasePath);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT source_window_title, source_url FROM captures WHERE id = $id;";
            command.Parameters.AddWithValue("$id", item.Id);
            using var reader = command.ExecuteReader();
            Assert.IsTrue(reader.Read());
            Assert.AreEqual("Invoice editor", reader.GetString(0));
            Assert.AreEqual("https://example.test/edit", reader.GetString(1));
        });
    }

    private static void CreateLegacyDatabase(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE captures (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                created_at TEXT NOT NULL,
                file_path TEXT NOT NULL,
                thumbnail_path TEXT NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                bytes INTEGER NOT NULL,
                is_private INTEGER NOT NULL,
                source_app TEXT,
                hotkey_profile TEXT,
                ocr_text TEXT,
                receipt_id TEXT,
                source_receipt_id TEXT,
                artifact_role TEXT,
                is_original INTEGER NOT NULL DEFAULT 0,
                source_available INTEGER NOT NULL DEFAULT 1,
                integrity_status TEXT,
                notes TEXT
            );
            CREATE VIRTUAL TABLE captures_fts USING fts5(
                id UNINDEXED,
                file_name,
                kind,
                source_app,
                hotkey_profile,
                notes,
                ocr_text,
                file_path
            );
            INSERT INTO captures_fts (id, file_name, kind, source_app, hotkey_profile, notes, ocr_text, file_path)
            VALUES ('legacy', 'legacy.png', 'Region', '', '', '', 'legacy text', 'C:\legacy.png');
            """;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        connection.Open();
        return connection;
    }

    private static List<string> ReadColumns(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static void WithTempPaths(Action<AppPaths> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        var originalLocal = Environment.GetEnvironmentVariable("RECEIPTS_LOCAL_ROOT");
        var originalLibrary = Environment.GetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("RECEIPTS_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT", Path.Combine(root, "library"));
            action(AppPaths.Create(new AppSettings()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RECEIPTS_LOCAL_ROOT", originalLocal);
            Environment.SetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT", originalLibrary);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
