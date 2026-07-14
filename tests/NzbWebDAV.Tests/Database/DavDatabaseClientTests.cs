using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Interceptors;
using NzbWebDAV.Database.MigrationHelpers;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Config;
using NzbWebDAV.Queue;
using NzbWebDAV.Tests.Fakes;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Tests.Database;

public sealed class DavDatabaseClientTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"nzbdav-tests-{Guid.NewGuid():N}.sqlite");
    private DavDatabaseContext _context = null!;
    private DavDatabaseClient _client = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<DavDatabaseContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .AddInterceptors(new SqliteForeignKeyEnabler())
            .ReplaceService<
                IMigrationsSqlGenerator,
                SqliteMigrationsSqlGenerator<SqliteMigrationsSqlGenerator>>()
            .Options;
        _context = new DavDatabaseContext(options);
        await _context.Database.MigrateAsync();
        _client = new DavDatabaseClient(_context);
    }

    [Fact]
    public async Task DirectoryQueriesAndRecursiveSize_UseRealSqliteSchema()
    {
        // the root item is already seeded by the database migrations
        var directory = DavItem.New(
            Guid.NewGuid(), DavItem.Root, "movies", null,
            DavItem.ItemType.Directory, DavItem.ItemSubType.Directory,
            null, null, null, null);
        var nestedDirectory = DavItem.New(
            Guid.NewGuid(), directory, "science-fiction", null,
            DavItem.ItemType.Directory, DavItem.ItemSubType.Directory,
            null, null, null, null);
        var firstFile = DavItem.New(
            Guid.NewGuid(), directory, "first.mkv", 100,
            DavItem.ItemType.UsenetFile, DavItem.ItemSubType.NzbFile,
            null, null, null, null);
        var nestedFile = DavItem.New(
            Guid.NewGuid(), nestedDirectory, "nested.mkv", 250,
            DavItem.ItemType.UsenetFile, DavItem.ItemSubType.NzbFile,
            null, null, null, null);

        _context.Items.AddRange(directory, nestedDirectory, firstFile, nestedFile);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var children = await _client.GetDirectoryChildrenAsync(directory.Id);
        Assert.Equal(
            new[] { "first.mkv", "science-fiction" },
            children.Select(item => item.Name));

        var streamedChildren = new List<DavItem>();
        await foreach (var child in _client.GetDirectoryChildrenEnumerableAsync(directory.Id))
            streamedChildren.Add(child);
        Assert.Equal(
            new[] { "first.mkv", "science-fiction" },
            streamedChildren.Select(item => item.Name));

        Assert.Equal(350, await _client.GetRecursiveSize(directory.Id));
        Assert.Equal(firstFile.Id, (await _client.GetFileById(firstFile.Id.ToString()))?.Id);
        Assert.Equal(
            firstFile.Id,
            (await _client.GetFilesByIdPrefix(firstFile.IdPrefix)).Single().Id);
    }

    [Fact]
    public async Task CompletedSymlinkCategoryChildren_AreDistinctAndOrdered()
    {
        var zetaDirectory = DavItem.New(
            Guid.NewGuid(), DavItem.ContentFolder, "zeta", null,
            DavItem.ItemType.Directory, DavItem.ItemSubType.Directory,
            null, null, null, null);
        var alphaDirectory = DavItem.New(
            Guid.NewGuid(), DavItem.ContentFolder, "alpha", null,
            DavItem.ItemType.Directory, DavItem.ItemSubType.Directory,
            null, null, null, null);
        var failedDirectory = DavItem.New(
            Guid.NewGuid(), DavItem.ContentFolder, "failed", null,
            DavItem.ItemType.Directory, DavItem.ItemSubType.Directory,
            null, null, null, null);

        _context.Items.AddRange(zetaDirectory, alphaDirectory, failedDirectory);
        _context.HistoryItems.AddRange(
            CreateHistoryItem("zeta.nzb", zetaDirectory.Id, HistoryItem.DownloadStatusOption.Completed),
            CreateHistoryItem("zeta-duplicate.nzb", zetaDirectory.Id, HistoryItem.DownloadStatusOption.Completed),
            CreateHistoryItem("alpha.nzb", alphaDirectory.Id, HistoryItem.DownloadStatusOption.Completed),
            CreateHistoryItem("failed.nzb", failedDirectory.Id, HistoryItem.DownloadStatusOption.Failed));
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var children = await _client.GetCompletedSymlinkCategoryChildren("movies");
        Assert.Equal(new[] { "alpha", "zeta" }, children.Select(item => item.Name));

        var streamedChildren = new List<DavItem>();
        await foreach (var child in _client.GetCompletedSymlinkCategoryChildrenEnumerableAsync("movies"))
            streamedChildren.Add(child);
        Assert.Equal(new[] { "alpha", "zeta" }, streamedChildren.Select(item => item.Name));
    }

    [Fact]
    public async Task GetFileById_NonGuidName_ReturnsNull()
    {
        Assert.Null(await _client.GetFileById("not-a-guid"));
        Assert.Null(await _client.GetFileById(".."));
        Assert.Null(await _client.GetFileById("favicon.ico"));
    }

    [Fact]
    public async Task GetItemByPathAsync_ResolvesNestedPersistedPaths()
    {
        var directory = DavItem.New(
            Guid.NewGuid(), DavItem.Root, "movies", null,
            DavItem.ItemType.Directory, DavItem.ItemSubType.Directory,
            null, null, null, null);
        var nestedDirectory = DavItem.New(
            Guid.NewGuid(), directory, "science-fiction", null,
            DavItem.ItemType.Directory, DavItem.ItemSubType.Directory,
            null, null, null, null);
        var nestedFile = DavItem.New(
            Guid.NewGuid(), nestedDirectory, "nested.mkv", 250,
            DavItem.ItemType.UsenetFile, DavItem.ItemSubType.NzbFile,
            null, null, null, null);

        _context.Items.AddRange(directory, nestedDirectory, nestedFile);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var hit = await _client.GetItemByPathAsync(nestedFile.Path);
        Assert.NotNull(hit);
        Assert.Equal(nestedFile.Id, hit.Id);
        Assert.Equal("/movies/science-fiction/nested.mkv", hit.Path);

        Assert.Null(await _client.GetItemByPathAsync("/movies/missing.mkv"));
    }

    [Fact]
    public async Task QueueItemProcessor_MovesMissingNzbToFailedHistory()
    {
        var queueItem = new QueueItem
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            FileName = "missing.nzb",
            JobName = "missing",
            NzbFileSize = 100,
            TotalSegmentBytes = 200,
            Category = "movies",
            Priority = QueueItem.PriorityOption.Normal,
            PostProcessing = QueueItem.PostProcessingOption.None
        };
        _context.QueueItems.Add(queueItem);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var processor = new QueueItemProcessor(
            queueItem,
            queueNzbStream: null,
            _client,
            new FakeNntpClient(new Dictionary<string, byte[]>()),
            new ConfigManager(),
            new WebsocketManager(),
            new Progress<int>(),
            CancellationToken.None);
        await processor.ProcessAsync();

        Assert.Empty(await _context.QueueItems.AsNoTracking().ToListAsync());
        var historyItem = Assert.Single(
            await _context.HistoryItems.AsNoTracking().ToListAsync());
        Assert.Equal(queueItem.Id, historyItem.Id);
        Assert.Equal(HistoryItem.DownloadStatusOption.Failed, historyItem.DownloadStatus);
        Assert.Equal("The NZB file could not be found.", historyItem.FailMessage);
    }

    private static HistoryItem CreateHistoryItem(
        string fileName,
        Guid downloadDirId,
        HistoryItem.DownloadStatusOption status)
    {
        return new HistoryItem
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            FileName = fileName,
            JobName = Path.GetFileNameWithoutExtension(fileName),
            Category = "movies",
            DownloadStatus = status,
            DownloadDirId = downloadDirId
        };
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        File.Delete(_databasePath);
    }
}
