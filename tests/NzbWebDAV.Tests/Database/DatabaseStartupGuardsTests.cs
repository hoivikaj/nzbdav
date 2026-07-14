using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Interceptors;
using NzbWebDAV.Database.MigrationHelpers;

namespace NzbWebDAV.Tests.Database;

public class DatabaseStartupGuardsTests
{
    [Theory]
    [InlineData(false, false, false, null, false)]
    [InlineData(true, false, true, null, false)] // empty WAL file: applied=none, pending=yes → fresh install
    [InlineData(true, true, true, null, true)] // real pre-0.6 DB without acknowledgement
    [InlineData(true, true, true, "0.6.0", false)] // acknowledged
    [InlineData(true, true, false, null, false)] // already past the breaking migration
    public void ShouldBlockUpgradeToV06X_MatchesFreshVsUpgradeSemantics(
        bool databaseFileExists,
        bool hasAppliedMigrations,
        bool hasPendingBreakingMigration,
        string? upgradeEnv,
        bool expectedBlock)
    {
        var applied = hasAppliedMigrations
            ? new[] { "20250529081501_InitializeDatabase" }
            : Array.Empty<string>();
        var pending = hasPendingBreakingMigration
            ? new[] { DatabaseStartupGuards.V06BreakingMigration }
            : Array.Empty<string>();

        Assert.Equal(
            expectedBlock,
            DatabaseStartupGuards.ShouldBlockUpgradeToV06X(
                databaseFileExists,
                applied,
                pending,
                upgradeEnv));
    }

    [Fact]
    public async Task ConfigItemsTableExistsAsync_ReturnsFalse_OnEmptySqliteFile()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"nzbdav-startup-{Guid.NewGuid():N}.sqlite");
        try
        {
            // Mimic a WAL-created empty file: open and close without migrating.
            await using (var bootstrap = new SqliteBootstrapContext(databasePath))
            {
                await bootstrap.Database.OpenConnectionAsync();
                await bootstrap.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
            }

            var options = new DbContextOptionsBuilder<DavDatabaseContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            await using var ctx = new DavDatabaseContext(options);
            Assert.False(await DatabaseStartupGuards.ConfigItemsTableExistsAsync(ctx));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
        }
    }

    [Fact]
    public async Task ConfigItemsTableExistsAsync_ReturnsTrue_AfterMigrate()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"nzbdav-startup-{Guid.NewGuid():N}.sqlite");
        try
        {
            var options = new DbContextOptionsBuilder<DavDatabaseContext>()
                .UseSqlite($"Data Source={databasePath}")
                .AddInterceptors(new SqliteMainDbPragmas())
                .ReplaceService<IMigrationsSqlGenerator, SqliteMigrationsSqlGenerator<SqliteMigrationsSqlGenerator>>()
                .Options;
            await using var ctx = new DavDatabaseContext(options);
            await ctx.Database.MigrateAsync();
            Assert.True(await DatabaseStartupGuards.ConfigItemsTableExistsAsync(ctx));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>
    /// Minimal context so we can create an empty sqlite file without EF migrations.
    /// </summary>
    private sealed class SqliteBootstrapContext(string path) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite($"Data Source={path}");
    }
}
