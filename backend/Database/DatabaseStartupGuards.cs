using Microsoft.EntityFrameworkCore;

namespace NzbWebDAV.Database;

/// <summary>
/// Shared checks used during process startup / --db-migration before the schema is known to exist.
/// </summary>
internal static class DatabaseStartupGuards
{
    /// <summary>
    /// True when the operational database has a <c>ConfigItems</c> table.
    /// Fresh / WAL-created empty files do not, so callers must not query config yet.
    /// </summary>
    public static async Task<bool> ConfigItemsTableExistsAsync(
        DbContext databaseContext,
        CancellationToken cancellationToken = default)
    {
        var count = await databaseContext.Database
            .SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS Value
                FROM sqlite_master
                WHERE type = 'table' AND name = 'ConfigItems'
                """)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        return count > 0;
    }
}
