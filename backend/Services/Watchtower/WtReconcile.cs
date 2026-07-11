using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Services;

public static class WtReconcile
{
    public static string ExpanderTag(string expanderKey) => "exp:" + expanderKey;

    public static async Task RemoveWithChildrenAsync(DavDatabaseContext ctx, WantedItem item, long now, CancellationToken ct)
    {
        if (item.State == WantedItem.StateExpander)
        {
            var tag = ExpanderTag(item.Key);
            var children = await ctx.WantedItems
                .Where(w => w.Provenance.Contains(tag))
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var child in children)
            {
                var prov = WtJson.ReadStrings(child.Provenance);
                if (!prov.Contains(tag)) continue;
                prov.Remove(tag);
                if (prov.Count == 0)
                {
                    ctx.WantedItems.Remove(child);
                }
                else
                {
                    child.Provenance = WtJson.WriteStrings(prov);
                    child.UpdatedAtUnix = now;
                }
            }
        }

        ctx.WantedItems.Remove(item);
    }

    public static async Task RemoveWithChildrenByRefAsync(
        DavDatabaseContext ctx, Guid id, string key, string state, long now, CancellationToken ct)
    {
        if (state == WantedItem.StateExpander)
        {
            var tag = ExpanderTag(key);
            var children = await ctx.WantedItems.AsNoTracking()
                .Where(w => w.Provenance.Contains(tag))
                .Select(w => new { w.Id, w.Provenance })
                .ToListAsync(ct).ConfigureAwait(false);

            var orphanIds = new List<Guid>();
            foreach (var child in children)
            {
                var prov = WtJson.ReadStrings(child.Provenance);
                if (!prov.Contains(tag)) continue;
                prov.Remove(tag);
                if (prov.Count == 0)
                    orphanIds.Add(child.Id);
                else
                    await ctx.WantedItems.Where(w => w.Id == child.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(w => w.Provenance, WtJson.WriteStrings(prov))
                            .SetProperty(w => w.UpdatedAtUnix, now), ct).ConfigureAwait(false);
            }
            await DeleteByIdsAsync(ctx, orphanIds, ct).ConfigureAwait(false);
        }

        await DeleteByIdsAsync(ctx, new[] { id }, ct).ConfigureAwait(false);
    }

    public static async Task DeleteByIdsAsync(DavDatabaseContext ctx, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        const int chunk = 400;
        for (var i = 0; i < ids.Count; i += chunk)
        {
            var slice = ids.Skip(i).Take(chunk).ToList();
            await ctx.WantedItems.Where(w => slice.Contains(w.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
    }
}
