using System.Text;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace NzbWebDAV.Database.Backup;

/// <summary>
/// Imports a <c>.sql</c> dump produced by <see cref="SqliteDumper"/> into a fresh
/// SQLite database file. Uses <c>sqlite3_complete</c> for statement splitting so
/// semicolons inside string literals are handled correctly.
/// </summary>
public static class SqliteSqlImporter
{
    private static int _batteriesInitialized;
    private static readonly strdelegate_authorizer RestoreAuthorizer = AuthorizeRestoreStatement;

    public static async Task ImportAsync(
        string sqlFilePath,
        string targetDatabaseFilePath,
        bool requireMigrationsHistory = false,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDatabaseFilePath);

        if (!File.Exists(sqlFilePath))
            throw new FileNotFoundException("SQL dump file not found.", sqlFilePath);

        if (File.Exists(targetDatabaseFilePath))
            throw new InvalidOperationException($"Target database already exists: {targetDatabaseFilePath}");

        EnsureBatteriesInitialized();

        var directory = Path.GetDirectoryName(targetDatabaseFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = targetDatabaseFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                DefaultTimeout = 30,
            }.ToString();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            raw.sqlite3_set_authorizer(connection.Handle, RestoreAuthorizer, null);

            progress?.Invoke("Importing SQL dump");
            await ExecuteStatementsAsync(sqlFilePath, connection, progress, cancellationToken).ConfigureAwait(false);

            progress?.Invoke("Running integrity check");
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA quick_check;";
                var result = (string?)await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"SQLite quick_check failed: {result}");
            }

            if (requireMigrationsHistory)
            {
                await using var history = connection.CreateCommand();
                history.CommandText = """
                    SELECT COUNT(*) FROM sqlite_master
                    WHERE type = 'table' AND name = '__EFMigrationsHistory';
                    """;
                var count = Convert.ToInt64(await history.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                if (count == 0)
                    throw new InvalidOperationException("Restored main database is missing __EFMigrationsHistory.");
            }
        }
        catch
        {
            TryDeleteDatabaseFiles(targetDatabaseFilePath);
            throw;
        }
    }

    private static int AuthorizeRestoreStatement(
        object? _,
        int actionCode,
        string? parameter1,
        string? parameter2,
        string? _databaseName,
        string? _triggerOrView)
    {
        if (actionCode is raw.SQLITE_ATTACH or raw.SQLITE_DETACH)
            return raw.SQLITE_DENY;

        if (actionCode == raw.SQLITE_FUNCTION &&
            string.Equals(parameter2, "load_extension", StringComparison.OrdinalIgnoreCase))
        {
            return raw.SQLITE_DENY;
        }

        if (actionCode == raw.SQLITE_PRAGMA &&
            string.Equals(parameter1, "writable_schema", StringComparison.OrdinalIgnoreCase))
        {
            return raw.SQLITE_DENY;
        }

        return raw.SQLITE_OK;
    }

    private static async Task ExecuteStatementsAsync(
        string sqlFilePath,
        SqliteConnection connection,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            sqlFilePath,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024);

        var buffer = new StringBuilder();
        var statementCount = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (buffer.Length > 0)
                buffer.Append('\n');
            buffer.Append(line);

            var candidate = buffer.ToString();
            if (!IsCompleteStatement(candidate))
                continue;

            var sql = candidate.Trim();
            buffer.Clear();
            if (sql.Length == 0)
                continue;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            statementCount++;
            if (statementCount % 500 == 0)
                progress?.Invoke($"Imported {statementCount} SQL statements");
        }

        var trailing = buffer.ToString().Trim();
        if (trailing.Length > 0)
        {
            if (!IsCompleteStatement(trailing) && !trailing.EndsWith(';'))
                trailing += ";";

            if (!IsCompleteStatement(trailing))
                throw new InvalidOperationException("SQL dump ended with an incomplete statement.");

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = trailing;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal static bool IsCompleteStatement(string sql)
    {
        EnsureBatteriesInitialized();
        return raw.sqlite3_complete(sql) != 0;
    }

    private static void EnsureBatteriesInitialized()
    {
        if (Interlocked.CompareExchange(ref _batteriesInitialized, 1, 0) == 0)
            Batteries_V2.Init();
    }

    private static void TryDeleteDatabaseFiles(string databaseFilePath)
    {
        foreach (var path in new[]
                 {
                     databaseFilePath,
                     databaseFilePath + "-wal",
                     databaseFilePath + "-shm",
                     databaseFilePath + "-journal",
                 })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup after a failed import.
            }
        }
    }
}
