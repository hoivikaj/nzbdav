using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NWebDav.Server.Stores;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue;
using NzbWebDAV.WebDav.Base;
using NzbWebDAV.WebDav.Requests;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreWatchFolder(
    DavItem davDirectory,
    DavDatabaseClient dbClient,
    ConfigManager configManager,
    QueueManager queueManager,
    WebsocketManager websocketManager
) : BaseStoreReadonlyCollection
{
    public override string Name => davDirectory.Name;
    public override string UniqueKey => davDirectory.Id.ToString();
    public override DateTime CreatedAt => davDirectory.CreatedAt;

    protected override async Task<IStoreItem?> GetItemAsync(GetItemRequest request)
    {
        var categories = await GetCategoriesAsync(request.CancellationToken).ConfigureAwait(false);
        if (!categories.Contains(request.Name)) return null;
        return new DatabaseStoreCategoryWatchFolder(
            request.Name, dbClient, configManager, queueManager, websocketManager);
    }

    protected override async IAsyncEnumerable<IStoreItem> GetAllItemsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var categories = await GetCategoriesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var category in categories)
        {
            yield return new DatabaseStoreCategoryWatchFolder(
                category, dbClient, configManager, queueManager, websocketManager);
        }
    }

    private async Task<IReadOnlySet<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var queueCategories = await dbClient.Ctx.QueueItems
            .Select(x => x.Category)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var configCategories = configManager.GetApiCategories();

        return queueCategories
            .Concat(configCategories)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet();
    }
}
