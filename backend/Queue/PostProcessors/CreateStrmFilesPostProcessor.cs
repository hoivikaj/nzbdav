using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.Controllers.GetWebdavItem;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;
using NzbWebDAV.WebDav;

namespace NzbWebDAV.Queue.PostProcessors;

public class CreateStrmFilesPostProcessor(
    ConfigManager configManager,
    DavDatabaseClient dbClient,
    Guid historyItemId)
{
    private static readonly string ContentRootPrefix =
        DavItem.ContentFolder.Path.TrimEnd('/') + "/";

    public async Task CreateStrmFilesAsync()
    {
        var candidates = CollectVideoItems();
        foreach (var videoItem in candidates)
            await CreateStrmFileAsync(videoItem).ConfigureAwait(false);
    }

    internal List<DavItem> CollectVideoItems()
    {
        var byId = new Dictionary<Guid, DavItem>();

        foreach (var item in dbClient.Ctx.ChangeTracker.Entries<DavItem>()
                     .Where(x => x.State == EntityState.Added)
                     .Select(x => x.Entity)
                     .Where(IsStrmCandidate))
        {
            byId[item.Id] = item;
        }

        foreach (var item in dbClient.Ctx.Items
                     .Where(x => x.HistoryItemId == historyItemId
                                 && x.Type != DavItem.ItemType.Directory)
                     .AsEnumerable()
                     .Where(IsStrmCandidate))
        {
            byId.TryAdd(item.Id, item);
        }

        return byId.Values.ToList();
    }

    private static bool IsStrmCandidate(DavItem item) =>
        FilenameUtil.IsVideoFile(item.Name)
        && !item.Name.EndsWith(".strm", StringComparison.OrdinalIgnoreCase);

    private async Task CreateStrmFileAsync(DavItem davItem)
    {
        var strmFilePath = GetStrmFilePath(davItem);
        var directoryName = Path.GetDirectoryName(strmFilePath);
        if (directoryName != null)
            await Task.Run(() => Directory.CreateDirectory(directoryName)).ConfigureAwait(false);

        var targetUrl = GetStrmTargetUrl(davItem);
        if (File.Exists(strmFilePath))
        {
            var existing = await File.ReadAllTextAsync(strmFilePath).ConfigureAwait(false);
            if (existing == targetUrl)
                return;
        }

        await File.WriteAllTextAsync(strmFilePath, targetUrl).ConfigureAwait(false);
    }

    internal string GetStrmFilePath(DavItem davItem)
    {
        var relativePath = GetPathRelativeToContentRoot(davItem.Path) + ".strm";
        return Path.Join(configManager.GetStrmCompletedDownloadDir(), relativePath);
    }

    internal static string GetPathRelativeToContentRoot(string davPath)
    {
        if (davPath.StartsWith(ContentRootPrefix, StringComparison.Ordinal))
            return davPath[ContentRootPrefix.Length..];

        // Fallback: preserve previous parts[2..] behavior for unexpected layouts.
        var parts = davPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 2 ? Path.Join(parts[2..]) : Path.GetFileName(davPath);
    }

    private string GetStrmTargetUrl(DavItem davItem)
    {
        var baseUrl = configManager.GetBaseUrl();
        if (baseUrl.EndsWith('/')) baseUrl = baseUrl.TrimEnd('/');
        var pathUrl = DatabaseStoreSymlinkFile.GetTargetPath(davItem.Id, "", '/');
        if (pathUrl.StartsWith('/')) pathUrl = pathUrl.TrimStart('/');
        var strmKey = configManager.GetStrmKey();
        var downloadKey = GetWebdavItemRequest.GenerateDownloadKey(strmKey, pathUrl);
        var extension = Path.GetExtension(davItem.Name).ToLower().TrimStart('.');
        return $"{baseUrl}/view/{pathUrl}?downloadKey={downloadKey}&extension={extension}";
    }
}
