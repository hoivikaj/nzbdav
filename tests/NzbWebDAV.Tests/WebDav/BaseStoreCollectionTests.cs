using NWebDav.Server;
using NWebDav.Server.Stores;
using NzbWebDAV.WebDav.Base;
using NzbWebDAV.WebDav.Requests;

namespace NzbWebDAV.Tests.WebDav;

public sealed class BaseStoreCollectionTests
{
    [Fact]
    public async Task ReadonlyCollection_RejectsEmptyPutWithoutAddingPhantomFile()
    {
        var collection = new ReadonlyCollection();

        var result = await collection.CreateItemAsync(
            "phantom.txt", Stream.Null, overwrite: false, CancellationToken.None);

        Assert.Equal(DavStatusCode.Forbidden, result.Result);
        Assert.Null(await collection.GetItemAsync("phantom.txt", CancellationToken.None));

        var items = new List<IStoreItem>();
        await foreach (var item in collection.GetItemsAsync(CancellationToken.None))
            items.Add(item);
        Assert.Empty(items);
    }

    [Fact]
    public async Task ReadonlyCollection_RejectsNonEmptyPut()
    {
        var collection = new ReadonlyCollection();
        await using var stream = new MemoryStream([1]);

        var result = await collection.CreateItemAsync(
            "file.txt", stream, overwrite: false, CancellationToken.None);

        Assert.Equal(DavStatusCode.Forbidden, result.Result);
    }

    [Fact]
    public void Collection_RejectsInfiniteDepth()
    {
        var collection = new ReadonlyCollection();

        Assert.Equal(InfiniteDepthMode.Rejected, collection.InfiniteDepthMode);
    }

    private sealed class ReadonlyCollection : BaseStoreReadonlyCollection
    {
        public override string Name => "root";
        public override string UniqueKey => "base-store-collection-tests";
        public override DateTime CreatedAt => DateTime.UnixEpoch;

        protected override Task<StoreItemResult> CopyAsync(CopyRequest request)
            => Task.FromResult(new StoreItemResult(DavStatusCode.Forbidden));

        protected override Task<IStoreItem?> GetItemAsync(GetItemRequest request)
            => Task.FromResult<IStoreItem?>(null);

        protected override async IAsyncEnumerable<IStoreItem> GetAllItemsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        protected override Task<StoreCollectionResult> CreateCollectionAsync(
            CreateCollectionRequest request)
            => Task.FromResult(new StoreCollectionResult(DavStatusCode.Forbidden));
    }
}
