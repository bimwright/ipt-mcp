using System;
using System.Collections.Generic;
using System.IO;
using Bimwright.Inventor.Shared.ToolBaker;
using Microsoft.Data.Sqlite;

namespace Bimwright.Inventor.Server.Bake;

/// <summary>
/// SQLite-backed ToolBaker store: the accepted-tool registry, adaptive suggestions, and usage events.
/// Ported verbatim from nwd-mcp with the product namespace rename. The schema is API-agnostic.
/// </summary>
public sealed class BakeDb : IDisposable
{
    private readonly string _dbPath;

    public BakeDb(string dbPath)
    {
        _dbPath = string.IsNullOrWhiteSpace(dbPath)
            ? throw new ArgumentException("Bake database path is required.", nameof(dbPath))
            : dbPath;
    }

    public void Dispose()
    {
    }

    public void Migrate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath) ?? ".");
        using var connection = OpenConnection();
        Execute(connection, @"
CREATE TABLE IF NOT EXISTS registry(
    name TEXT PRIMARY KEY,
    description TEXT NOT NULL,
    source TEXT NOT NULL,
    params_schema TEXT NOT NULL,
    compat_map TEXT NOT NULL,
    source_code TEXT NULL,
    handler_tool TEXT NULL,
    fixed_args TEXT NULL,
    sequence_json TEXT NULL,
    created_from_suggestion_id TEXT NULL,
    reviewed_by_user INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    last_used_at TEXT NULL,
    usage_count INTEGER NOT NULL DEFAULT 0,
    failure_rate REAL NOT NULL DEFAULT 0,
    lifecycle_state TEXT NOT NULL DEFAULT 'accepted',
    version_history_blob TEXT NOT NULL DEFAULT '[]'
)");
        Execute(connection, @"
CREATE TABLE IF NOT EXISTS suggestions(
    id TEXT PRIMARY KEY,
    cluster_key TEXT NOT NULL UNIQUE,
    source TEXT NOT NULL,
    title TEXT NOT NULL,
    description TEXT NOT NULL,
    state TEXT NOT NULL,
    score REAL NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    snooze_until TEXT NULL,
    never_reason TEXT NULL,
    payload_json TEXT NOT NULL,
    version_history_blob TEXT NOT NULL DEFAULT '[]'
)");
        Execute(connection, @"
CREATE TABLE IF NOT EXISTS usage_events(
    timestamp TEXT NOT NULL,
    session_id TEXT NOT NULL,
    tool TEXT NOT NULL,
    params_hash TEXT NOT NULL,
    ok INTEGER NOT NULL,
    duration_ms INTEGER NOT NULL
)");
    }

    public bool TryInsertRegistryRecord(BakedToolRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT OR IGNORE INTO registry(
    name, description, source, params_schema, compat_map, source_code, handler_tool,
    fixed_args, sequence_json, created_from_suggestion_id, reviewed_by_user, created_at,
    last_used_at, usage_count, failure_rate, lifecycle_state, version_history_blob
) VALUES (
    $name, $description, $source, $params_schema, $compat_map, $source_code, $handler_tool,
    $fixed_args, $sequence_json, $created_from_suggestion_id, $reviewed_by_user, $created_at,
    $last_used_at, $usage_count, $failure_rate, $lifecycle_state, $version_history_blob
)";
        BindRegistry(command, record);
        return command.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<BakedToolRecord> ReadRegistryRecords()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM registry ORDER BY name";
        using var reader = command.ExecuteReader();
        var records = new List<BakedToolRecord>();
        while (reader.Read())
        {
            records.Add(ReadRegistry(reader));
        }
        return records;
    }

    public BakedToolRecord? GetRegistryRecord(string name)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM registry WHERE name = $name";
        command.Parameters.AddWithValue("$name", name ?? string.Empty);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRegistry(reader) : null;
    }

    public bool UpsertSuggestion(BakeSuggestionRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (!BakeSuggestionStates.IsValid(record.State)) throw new ArgumentException("Invalid suggestion state.", nameof(record));
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO suggestions(
    id, cluster_key, source, title, description, state, score, created_at, updated_at,
    snooze_until, never_reason, payload_json, version_history_blob
) VALUES (
    $id, $cluster_key, $source, $title, $description, $state, $score, $created_at, $updated_at,
    $snooze_until, $never_reason, $payload_json, $version_history_blob
)
ON CONFLICT(id) DO UPDATE SET
    cluster_key = excluded.cluster_key,
    source = excluded.source,
    title = excluded.title,
    description = excluded.description,
    state = excluded.state,
    score = excluded.score,
    updated_at = excluded.updated_at,
    snooze_until = excluded.snooze_until,
    never_reason = excluded.never_reason,
    payload_json = excluded.payload_json,
    version_history_blob = excluded.version_history_blob";
        BindSuggestion(command, record);
        return command.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<BakeSuggestionRecord> ListSuggestions()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM suggestions ORDER BY created_at DESC, id";
        using var reader = command.ExecuteReader();
        var records = new List<BakeSuggestionRecord>();
        while (reader.Read())
        {
            records.Add(ReadSuggestion(reader));
        }
        return records;
    }

    public BakeSuggestionRecord? GetSuggestion(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM suggestions WHERE id = $id";
        command.Parameters.AddWithValue("$id", id ?? string.Empty);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSuggestion(reader) : null;
    }

    public bool TryUpdateSuggestionState(string id, string state)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE suggestions SET state = $state, updated_at = $updated_at WHERE id = $id";
        command.Parameters.AddWithValue("$id", id ?? string.Empty);
        command.Parameters.AddWithValue("$state", state ?? BakeSuggestionStates.Open);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("o"));
        return command.ExecuteNonQuery() == 1;
    }

    public void InsertUsageEvent(UsageEvent usageEvent)
    {
        if (usageEvent == null) throw new ArgumentNullException(nameof(usageEvent));
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO usage_events(timestamp, session_id, tool, params_hash, ok, duration_ms)
VALUES($timestamp, $session_id, $tool, $params_hash, $ok, $duration_ms)";
        Add(command, "$timestamp", string.IsNullOrWhiteSpace(usageEvent.Timestamp) ? DateTimeOffset.UtcNow.ToString("o") : usageEvent.Timestamp);
        Add(command, "$session_id", usageEvent.SessionId ?? "server");
        Add(command, "$tool", usageEvent.Tool);
        Add(command, "$params_hash", usageEvent.ParamsHash ?? usageEvent.NormalizedKey ?? string.Empty);
        command.Parameters.AddWithValue("$ok", usageEvent.Success ? 1 : 0);
        command.Parameters.AddWithValue("$duration_ms", usageEvent.DurationMs);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("Data Source=" + _dbPath);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void BindRegistry(SqliteCommand command, BakedToolRecord record)
    {
        Add(command, "$name", record.Name);
        Add(command, "$description", record.Description);
        Add(command, "$source", record.Source);
        Add(command, "$params_schema", record.ParamsSchema ?? "{}");
        Add(command, "$compat_map", record.CompatMap ?? "{}");
        Add(command, "$source_code", record.SourceCode);
        Add(command, "$handler_tool", record.HandlerTool);
        Add(command, "$fixed_args", record.FixedArgs ?? "{}");
        Add(command, "$sequence_json", record.Sequence ?? "[]");
        Add(command, "$created_from_suggestion_id", record.CreatedFromSuggestionId);
        command.Parameters.AddWithValue("$reviewed_by_user", record.ReviewedByUser ? 1 : 0);
        Add(command, "$created_at", string.IsNullOrWhiteSpace(record.CreatedAt) ? DateTimeOffset.UtcNow.ToString("o") : record.CreatedAt);
        Add(command, "$last_used_at", record.LastUsedAt);
        command.Parameters.AddWithValue("$usage_count", record.UsageCount);
        command.Parameters.AddWithValue("$failure_rate", record.FailureRate);
        Add(command, "$lifecycle_state", record.LifecycleState ?? "accepted");
        Add(command, "$version_history_blob", record.VersionHistoryBlob ?? "[]");
    }

    private static void BindSuggestion(SqliteCommand command, BakeSuggestionRecord record)
    {
        Add(command, "$id", record.Id);
        Add(command, "$cluster_key", record.ClusterKey);
        Add(command, "$source", record.Source);
        Add(command, "$title", record.Title);
        Add(command, "$description", record.Description);
        Add(command, "$state", record.State);
        command.Parameters.AddWithValue("$score", record.Score);
        Add(command, "$created_at", record.CreatedAt ?? DateTimeOffset.UtcNow.ToString("o"));
        Add(command, "$updated_at", record.UpdatedAt ?? DateTimeOffset.UtcNow.ToString("o"));
        Add(command, "$snooze_until", record.SnoozeUntil);
        Add(command, "$never_reason", record.NeverReason);
        Add(command, "$payload_json", record.PayloadJson ?? "{}");
        Add(command, "$version_history_blob", record.VersionHistoryBlob ?? "[]");
    }

    private static BakedToolRecord ReadRegistry(SqliteDataReader reader)
    {
        return new BakedToolRecord
        {
            Name = Text(reader, "name") ?? "",
            Description = Text(reader, "description") ?? "",
            Source = Text(reader, "source") ?? "",
            ParamsSchema = Text(reader, "params_schema") ?? "{}",
            CompatMap = Text(reader, "compat_map") ?? "{}",
            SourceCode = Text(reader, "source_code") ?? "",
            HandlerTool = Text(reader, "handler_tool") ?? "",
            FixedArgs = Text(reader, "fixed_args") ?? "{}",
            Sequence = Text(reader, "sequence_json") ?? "[]",
            CreatedFromSuggestionId = Text(reader, "created_from_suggestion_id"),
            ReviewedByUser = reader.GetInt32(reader.GetOrdinal("reviewed_by_user")) == 1,
            CreatedAt = Text(reader, "created_at") ?? "",
            LastUsedAt = Text(reader, "last_used_at"),
            UsageCount = reader.GetInt32(reader.GetOrdinal("usage_count")),
            FailureRate = reader.GetDouble(reader.GetOrdinal("failure_rate")),
            LifecycleState = Text(reader, "lifecycle_state") ?? "accepted",
            VersionHistoryBlob = Text(reader, "version_history_blob") ?? "[]"
        };
    }

    private static BakeSuggestionRecord ReadSuggestion(SqliteDataReader reader)
    {
        return new BakeSuggestionRecord
        {
            Id = Text(reader, "id") ?? "",
            ClusterKey = Text(reader, "cluster_key") ?? "",
            Source = Text(reader, "source") ?? "",
            Title = Text(reader, "title") ?? "",
            Description = Text(reader, "description") ?? "",
            State = Text(reader, "state") ?? "",
            Score = reader.GetDouble(reader.GetOrdinal("score")),
            CreatedAt = Text(reader, "created_at"),
            UpdatedAt = Text(reader, "updated_at"),
            SnoozeUntil = Text(reader, "snooze_until"),
            NeverReason = Text(reader, "never_reason"),
            PayloadJson = Text(reader, "payload_json") ?? "{}",
            VersionHistoryBlob = Text(reader, "version_history_blob") ?? "[]"
        };
    }

    private static void Add(SqliteCommand command, string name, string? value)
        => command.Parameters.AddWithValue(name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value);

    private static string? Text(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
