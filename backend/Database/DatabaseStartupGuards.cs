using Microsoft.EntityFrameworkCore;

namespace NzbWebDAV.Database;

/// <summary>
/// Shared checks used during process startup / --db-migration before the schema is known to exist.
/// </summary>
internal static class DatabaseStartupGuards
{
    public const string V06BreakingMigration = "20260226053712_Add-NzbBlobId-And-NzbNames";

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

    /// <summary>
    /// Whether startup should refuse to continue until the operator sets <c>UPGRADE=0.6.0</c>.
    /// A WAL-created empty <c>db.sqlite</c> (no applied migrations) is a fresh install, not an upgrade.
    /// </summary>
    public static bool ShouldBlockUpgradeToV06X(
        bool databaseFileExists,
        IEnumerable<string> appliedMigrations,
        IEnumerable<string> pendingMigrations,
        string? upgradeEnv)
    {
        if (!databaseFileExists)
            return false;

        if (!appliedMigrations.Any())
            return false;

        if (!pendingMigrations.Contains(V06BreakingMigration))
            return false;

        return upgradeEnv != "0.6.0";
    }
}
