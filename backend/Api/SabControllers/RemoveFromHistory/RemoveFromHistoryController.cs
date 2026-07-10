using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.SabControllers.RemoveFromHistory;

public class RemoveFromHistoryController(
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    ConfigManager configManager,
    WebsocketManager websocketManager
) : SabApiController.BaseController(httpContext, configManager)
{
    public async Task<RemoveFromHistoryResponse> RemoveFromHistory(RemoveFromHistoryRequest request)
    {
        await dbClient.RemoveHistoryItemsAsync(request.NzoIds, request.DeleteCompletedFiles, request.CancellationToken).ConfigureAwait(false);
        try
        {
            await dbClient.Ctx.SaveChangesAsync(request.CancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.All(e => e.Entity is HistoryItem))
        {
            // A HistoryItem vanished between RemoveHistoryItemsAsync's existence check and this save
            // (a concurrent delete). The SAB API delete is idempotent, so that outcome is success.
            //
            // The `when` filter matters: on the deleteFiles=true path this SaveChanges also removes
            // joined DavItem rows. A concurrency conflict on THOSE is a real conflict, not an
            // already-deleted history row, and must not be swallowed.
        }
        _ = websocketManager.SendMessage(WebsocketTopic.HistoryItemRemoved, string.Join(",", request.NzoIds));
        return new RemoveFromHistoryResponse() { Status = true };
    }

    protected override async Task<IActionResult> Handle()
    {
        var request = await RemoveFromHistoryRequest.New(httpContext).ConfigureAwait(false);
        return Ok(await RemoveFromHistory(request).ConfigureAwait(false));
    }
}