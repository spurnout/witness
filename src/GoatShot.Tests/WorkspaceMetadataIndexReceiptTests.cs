using GoatShot.App.Models;
using GoatShot.App.Services;
using Microsoft.Data.Sqlite;

namespace GoatShot.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WorkspaceMetadataIndexReceiptTests
{
    [TestMethod]
    public void EnsureCreated_MigratesLegacyRowsAndUpsertsReceiptMetadata()
    {
        WithTempPaths(paths =>
        {
            CreateLegacyDatabase(paths.MetadataDatabasePath);
            var index = new WorkspaceMetadataIndex(paths);

            index.EnsureCreated();
            index.EnsureCreated();

            using var connection = Open(paths.MetadataDatabasePath);
            var columns = ReadColumns(connection, "captures");
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "receipt_id",
                    "source_receipt_id",
                    "artifact_role",
                    "is_original",
                    "source_available",
                    "integrity_status"
                },
                columns.ToArray());

            using (var legacy = connection.CreateCommand())
            {
                legacy.CommandText =
                    "SELECT is_original, source_available, receipt_id, source_receipt_id, artifact_role, integrity_status " +
                    "FROM captures WHERE id = 'legacy';";
                using var reader = legacy.ExecuteReader();
                Assert.IsTrue(reader.Read());
                Assert.AreEqual(0L, reader.GetInt64(0));
                Assert.AreEqual(1L, reader.GetInt64(1));
                Assert.IsTrue(reader.IsDBNull(2));
                Assert.IsTrue(reader.IsDBNull(3));
                Assert.IsTrue(reader.IsDBNull(4));
                Assert.IsTrue(reader.IsDBNull(5));
            }

            var item = new CaptureItem
            {
                Id = "receipt-derivative",
                Kind = CaptureKind.VideoFrame,
                CreatedAt = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
                FilePath = Path.Combine(paths.ImagesRoot, "derived.png"),
                ThumbnailPath = Path.Combine(paths.ThumbnailRoot, "derived.jpg"),
                Bytes = 123,
                ReceiptId = "receipt-original",
                SourceReceiptId = "receipt-parent",
                ArtifactRole = "unique-frame",
                IsOriginal = false,
                SourceAvailable = false,
                IntegrityStatus = "Intact — known device key"
            };
            index.Upsert(item);

            using var upserted = connection.CreateCommand();
            upserted.CommandText =
                "SELECT receipt_id, source_receipt_id, artifact_role, is_original, source_available, integrity_status " +
                "FROM captures WHERE id = $id;";
            upserted.Parameters.AddWithValue("$id", item.Id);
            using var upsertedReader = upserted.ExecuteReader();
            Assert.IsTrue(upsertedReader.Read());
            Assert.AreEqual(item.ReceiptId, upsertedReader.GetString(0));
            Assert.AreEqual(item.SourceReceiptId, upsertedReader.GetString(1));
            Assert.AreEqual(item.ArtifactRole, upsertedReader.GetString(2));
            Assert.AreEqual(0L, upsertedReader.GetInt64(3));
            Assert.AreEqual(0L, upsertedReader.GetInt64(4));
            Assert.AreEqual(item.IntegrityStatus, upsertedReader.GetString(5));
        });
    }

    private static void CreateLegacyDatabase(string databasePath)
    {
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
                notes TEXT
            );
            INSERT INTO captures (
                id, kind, created_at, file_path, thumbnail_path, width, height,
                bytes, is_private, source_app, hotkey_profile, ocr_text, notes
            ) VALUES (
                'legacy', 'Fullscreen', '2026-08-10T12:00:00.0000000+00:00',
                'legacy.png', 'legacy.jpg', 100, 100, 50, 0, '', '', '', 'legacy row'
            );
            """;
        command.ExecuteNonQuery();
    }

    private static HashSet<string> ReadColumns(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        connection.Open();
        return connection;
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
