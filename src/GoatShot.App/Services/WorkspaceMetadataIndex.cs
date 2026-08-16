using System.Text.RegularExpressions;
using GoatShot.App.Models;
using Microsoft.Data.Sqlite;

namespace GoatShot.App.Services;

public sealed partial class WorkspaceMetadataIndex
{
    private readonly AppPaths _paths;
    private readonly object _gate = new();

    public WorkspaceMetadataIndex(AppPaths paths)
    {
        _paths = paths;
    }

    public string DatabasePath => _paths.MetadataDatabasePath;

    public void EnsureCreated()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS captures (
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
                    notes TEXT,
                    source_window_title TEXT,
                    source_url TEXT
                );
                CREATE VIRTUAL TABLE IF NOT EXISTS captures_fts USING fts5(
                    id UNINDEXED,
                    file_name,
                    kind,
                    source_app,
                    source_window_title,
                    source_url,
                    hotkey_profile,
                    notes,
                    ocr_text,
                    file_path
                );
                """;
            command.ExecuteNonQuery();
            EnsureColumn(connection, "captures", "hotkey_profile", "TEXT");
            EnsureColumn(connection, "captures", "receipt_id", "TEXT");
            EnsureColumn(connection, "captures", "source_receipt_id", "TEXT");
            EnsureColumn(connection, "captures", "artifact_role", "TEXT");
            EnsureColumn(connection, "captures", "is_original", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "captures", "source_available", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(connection, "captures", "integrity_status", "TEXT");
            EnsureColumn(connection, "captures", "source_window_title", "TEXT");
            EnsureColumn(connection, "captures", "source_url", "TEXT");
            EnsureFtsSchema(connection);
        }
    }

    public void Rebuild(IEnumerable<CaptureItem> items)
    {
        lock (_gate)
        {
            EnsureCreated();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            ExecuteNonQuery(connection, transaction, "DELETE FROM captures;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM captures_fts;");
            foreach (var item in items.Where(item => !item.IsPrivate))
            {
                UpsertCore(connection, transaction, item);
            }

            transaction.Commit();
        }
    }

    public void Upsert(CaptureItem item)
    {
        if (item.IsPrivate)
        {
            return;
        }

        lock (_gate)
        {
            EnsureCreated();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertCore(connection, transaction, item);
            transaction.Commit();
        }
    }

    public void Delete(CaptureItem item)
    {
        lock (_gate)
        {
            EnsureCreated();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var deleteCaptures = connection.CreateCommand();
            deleteCaptures.Transaction = transaction;
            deleteCaptures.CommandText = "DELETE FROM captures WHERE id = $id OR file_path = $file_path;";
            deleteCaptures.Parameters.AddWithValue("$id", item.Id);
            deleteCaptures.Parameters.AddWithValue("$file_path", item.FilePath);
            deleteCaptures.ExecuteNonQuery();

            using var deleteFts = connection.CreateCommand();
            deleteFts.Transaction = transaction;
            deleteFts.CommandText = "DELETE FROM captures_fts WHERE id = $id OR file_path = $file_path;";
            deleteFts.Parameters.AddWithValue("$id", item.Id);
            deleteFts.Parameters.AddWithValue("$file_path", item.FilePath);
            deleteFts.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    public IReadOnlyList<string> SearchIds(string query, int limit = 200)
    {
        return Search(query, limit)
            .Select(hit => hit.Id)
            .ToList();
    }

    public IReadOnlyList<WorkspaceSearchHit> Search(string query, int limit = 200)
    {
        var ftsQuery = BuildFtsQuery(query);
        if (string.IsNullOrWhiteSpace(ftsQuery))
        {
            return Array.Empty<WorkspaceSearchHit>();
        }

        lock (_gate)
        {
            EnsureCreated();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT c.id,
                       c.file_path,
                       f.file_name,
                       c.kind,
                       c.created_at,
                       snippet(captures_fts, -1, '', '', '...', 16) AS snippet
                FROM captures_fts f
                JOIN captures c ON c.id = f.id
                WHERE captures_fts MATCH $query
                ORDER BY rank, c.created_at DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$query", ftsQuery);
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

            var hits = new List<WorkspaceSearchHit>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                hits.Add(new WorkspaceSearchHit
                {
                    Id = reader.GetString(0),
                    FilePath = reader.GetString(1),
                    FileName = reader.GetString(2),
                    Kind = reader.GetString(3),
                    CreatedAt = reader.GetString(4),
                    Snippet = reader.IsDBNull(5) ? string.Empty : SensitiveTextDetector.Redact(reader.GetString(5))
                });
            }

            return hits;
        }
    }

    public string GetStatus()
    {
        try
        {
            lock (_gate)
            {
                EnsureCreated();
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM captures;";
                var count = Convert.ToInt64(command.ExecuteScalar());
                return $"SQLite metadata index is active at {DatabasePath} with {count} capture row(s).";
            }
        }
        catch (Exception ex)
        {
            return $"SQLite metadata index is unavailable: {ex.Message}";
        }
    }

    private void UpsertCore(SqliteConnection connection, SqliteTransaction transaction, CaptureItem item)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO captures (
                    id, kind, created_at, file_path, thumbnail_path, width, height, bytes,
                    is_private, source_app, source_window_title, source_url, hotkey_profile,
                    ocr_text, receipt_id, source_receipt_id, artifact_role, is_original,
                    source_available, integrity_status, notes
                )
                VALUES (
                    $id, $kind, $created_at, $file_path, $thumbnail_path, $width, $height, $bytes,
                    $is_private, $source_app, $source_window_title, $source_url, $hotkey_profile,
                    $ocr_text, $receipt_id, $source_receipt_id, $artifact_role, $is_original,
                    $source_available, $integrity_status, $notes
                )
                ON CONFLICT(id) DO UPDATE SET
                    kind = excluded.kind,
                    created_at = excluded.created_at,
                    file_path = excluded.file_path,
                    thumbnail_path = excluded.thumbnail_path,
                    width = excluded.width,
                    height = excluded.height,
                    bytes = excluded.bytes,
                    is_private = excluded.is_private,
                    source_app = excluded.source_app,
                    source_window_title = excluded.source_window_title,
                    source_url = excluded.source_url,
                    hotkey_profile = excluded.hotkey_profile,
                    ocr_text = excluded.ocr_text,
                    receipt_id = excluded.receipt_id,
                    source_receipt_id = excluded.source_receipt_id,
                    artifact_role = excluded.artifact_role,
                    is_original = excluded.is_original,
                    source_available = excluded.source_available,
                    integrity_status = excluded.integrity_status,
                    notes = excluded.notes;
                """;
            AddCaptureParameters(command, item);
            command.ExecuteNonQuery();
        }

        ExecuteNonQuery(connection, transaction, "DELETE FROM captures_fts WHERE id = $id;", ("$id", item.Id));

        using var ftsCommand = connection.CreateCommand();
        ftsCommand.Transaction = transaction;
        ftsCommand.CommandText =
            """
            INSERT INTO captures_fts (id, file_name, kind, source_app, source_window_title, source_url, hotkey_profile, notes, ocr_text, file_path)
            VALUES ($id, $file_name, $kind, $source_app, $source_window_title, $source_url, $hotkey_profile, $notes, $ocr_text, $file_path);
            """;
        ftsCommand.Parameters.AddWithValue("$id", item.Id);
        ftsCommand.Parameters.AddWithValue("$file_name", item.FileName);
        ftsCommand.Parameters.AddWithValue("$kind", item.Kind.ToString());
        ftsCommand.Parameters.AddWithValue("$source_app", item.SourceApp ?? string.Empty);
        ftsCommand.Parameters.AddWithValue("$source_window_title", item.SourceWindowTitle ?? string.Empty);
        ftsCommand.Parameters.AddWithValue("$source_url", item.SourceUrl ?? string.Empty);
        ftsCommand.Parameters.AddWithValue("$hotkey_profile", item.HotkeyProfile ?? string.Empty);
        ftsCommand.Parameters.AddWithValue("$notes", item.Notes ?? string.Empty);
        ftsCommand.Parameters.AddWithValue("$ocr_text", item.OcrText ?? string.Empty);
        ftsCommand.Parameters.AddWithValue("$file_path", item.FilePath);
        ftsCommand.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void AddCaptureParameters(SqliteCommand command, CaptureItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$kind", item.Kind.ToString());
        command.Parameters.AddWithValue("$created_at", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$file_path", item.FilePath);
        command.Parameters.AddWithValue("$thumbnail_path", item.ThumbnailPath);
        command.Parameters.AddWithValue("$width", item.Width);
        command.Parameters.AddWithValue("$height", item.Height);
        command.Parameters.AddWithValue("$bytes", item.Bytes);
        command.Parameters.AddWithValue("$is_private", item.IsPrivate ? 1 : 0);
        command.Parameters.AddWithValue("$source_app", item.SourceApp ?? string.Empty);
        command.Parameters.AddWithValue("$source_window_title", item.SourceWindowTitle ?? string.Empty);
        command.Parameters.AddWithValue("$source_url", item.SourceUrl ?? string.Empty);
        command.Parameters.AddWithValue("$hotkey_profile", item.HotkeyProfile ?? string.Empty);
        command.Parameters.AddWithValue("$ocr_text", item.OcrText ?? string.Empty);
        command.Parameters.AddWithValue("$receipt_id", item.ReceiptId ?? string.Empty);
        command.Parameters.AddWithValue("$source_receipt_id", item.SourceReceiptId ?? string.Empty);
        command.Parameters.AddWithValue("$artifact_role", item.ArtifactRole ?? string.Empty);
        command.Parameters.AddWithValue("$is_original", item.IsOriginal ? 1 : 0);
        command.Parameters.AddWithValue("$source_available", item.SourceAvailable ? 1 : 0);
        command.Parameters.AddWithValue("$integrity_status", item.IntegrityStatus ?? string.Empty);
        command.Parameters.AddWithValue("$notes", item.Notes ?? string.Empty);
    }

    private static void EnsureColumn(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string definition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }

    private static void EnsureFtsSchema(SqliteConnection connection)
    {
        // Sentinel column: the newest FTS column doubles as the schema marker. FTS5 has no
        // ALTER TABLE, so an old table is dropped and recreated; the startup Rebuild that
        // called into here repopulates it in the same pass.
        if (VirtualTableHasColumn(connection, "captures_fts", "source_url"))
        {
            return;
        }

        using var drop = connection.CreateCommand();
        drop.CommandText = "DROP TABLE IF EXISTS captures_fts;";
        drop.ExecuteNonQuery();

        using var create = connection.CreateCommand();
        create.CommandText =
            """
            CREATE VIRTUAL TABLE captures_fts USING fts5(
                id UNINDEXED,
                file_name,
                kind,
                source_app,
                source_window_title,
                source_url,
                hotkey_profile,
                notes,
                ocr_text,
                file_path
            );
            """;
        create.ExecuteNonQuery();
    }

    private static bool VirtualTableHasColumn(
        SqliteConnection connection,
        string tableName,
        string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        command.ExecuteNonQuery();
    }

    private static string BuildFtsQuery(string query)
    {
        var tokens = SearchTokenRegex()
            .Matches(query)
            .Select(match => match.Value)
            .Where(token => token.Length > 0)
            .Take(12)
            .Select(token => $"{token}*")
            .ToList();

        return tokens.Count == 0 ? string.Empty : string.Join(" AND ", tokens);
    }

    [GeneratedRegex(@"[\p{L}\p{N}_]+")]
    private static partial Regex SearchTokenRegex();
}
