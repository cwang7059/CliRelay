using Microsoft.Data.Sqlite;

var mode = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0].Trim().ToLowerInvariant() : "failed";
var value = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1].Trim() : "new106";
var dbPath = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2])
    ? args[2].Trim()
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "usage.db"));

if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found: {dbPath}");
    Environment.Exit(1);
}

var whereClause = mode switch
{
    "failed" => "failed = 1",
    "channel" => "trim(channel_name) = $value",
    "list-settings" => string.Empty,
    "list-openai-compat" => string.Empty,
    _ => throw new InvalidOperationException($"Unsupported mode: {mode}")
};

var builder = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadWrite,
    ForeignKeys = true,
    DefaultTimeout = 5,
};

using var connection = new SqliteConnection(builder.ConnectionString);
connection.Open();

using (var pragma = connection.CreateCommand())
{
    pragma.CommandText = "PRAGMA busy_timeout = 5000;";
    pragma.ExecuteNonQuery();
}

if (mode == "list-openai-compat")
{
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT payload FROM runtime_settings WHERE setting_key = 'openai-compatibility'";
    var payload = command.ExecuteScalar() as string;
    Console.WriteLine(payload ?? string.Empty);
    return;
}

if (mode == "list-settings")
{
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT setting_key, payload FROM runtime_settings ORDER BY setting_key";
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        var key = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        var payload = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        Console.WriteLine($"KEY={key}");
        Console.WriteLine(payload);
    }
    return;
}

var beforeLogs = ExecuteCount(connection, $"SELECT COUNT(*) FROM request_logs WHERE {whereClause}", mode, value);
var beforeContents = ExecuteCount(connection, $@"
SELECT COUNT(*)
FROM request_log_content
WHERE log_id IN (
    SELECT id FROM request_logs WHERE {whereClause}
)", mode, value);

Console.WriteLine($"mode={mode}");
Console.WriteLine($"value={value}");
Console.WriteLine($"db={dbPath}");
Console.WriteLine($"before.request_logs={beforeLogs}");
Console.WriteLine($"before.request_log_content={beforeContents}");

if (beforeLogs == 0)
{
    Console.WriteLine("Nothing to delete.");
    return;
}

using var transaction = connection.BeginTransaction();

using var deleteContent = connection.CreateCommand();
deleteContent.Transaction = transaction;
deleteContent.CommandText = $@"
DELETE FROM request_log_content
WHERE log_id IN (
    SELECT id FROM request_logs WHERE {whereClause}
)";
BindParameters(deleteContent, mode, value);
var deletedContents = deleteContent.ExecuteNonQuery();

using var deleteLogs = connection.CreateCommand();
deleteLogs.Transaction = transaction;
deleteLogs.CommandText = $"DELETE FROM request_logs WHERE {whereClause}";
BindParameters(deleteLogs, mode, value);
var deletedLogs = deleteLogs.ExecuteNonQuery();

transaction.Commit();

var afterLogs = ExecuteCount(connection, $"SELECT COUNT(*) FROM request_logs WHERE {whereClause}", mode, value);
var afterContents = ExecuteCount(connection, $@"
SELECT COUNT(*)
FROM request_log_content
WHERE log_id IN (
    SELECT id FROM request_logs WHERE {whereClause}
)", mode, value);

Console.WriteLine($"deleted.request_logs={deletedLogs}");
Console.WriteLine($"deleted.request_log_content={deletedContents}");
Console.WriteLine($"after.request_logs={afterLogs}");
Console.WriteLine($"after.request_log_content={afterContents}");

static long ExecuteCount(SqliteConnection connection, string sql, string mode, string value)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    BindParameters(command, mode, value);
    return (long)(command.ExecuteScalar() ?? 0L);
}

static void BindParameters(SqliteCommand command, string mode, string value)
{
    if (mode == "channel")
    {
        command.Parameters.AddWithValue("$value", value);
    }
}
