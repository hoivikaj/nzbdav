using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Queue;
using NzbWebDAV.Services;
using NzbWebDAV.Services.Benchmark;

namespace NzbWebDAV.Api.Controllers.BenchmarkUsenetConnection;

[ApiController]
[Route("api/benchmark-usenet-connection")]
public class BenchmarkUsenetConnectionController(
    UsenetBenchmarkService benchmarkService,
    BenchmarkGate benchmarkGate,
    ActiveReadRegistry activeReads,
    QueueManager queueManager,
    ConfigManager configManager
) : BaseApiController
{
    private async Task<BenchmarkUsenetConnectionResponse> BenchmarkAsync(BenchmarkUsenetConnectionRequest request)
    {
        try
        {
            // Pause the download queue + background verifiers for the duration so
            // the test gets the provider's full connection budget. The gate is
            // released on dispose — including on cancel or error.
            using var pause = benchmarkGate.Enter();

            // Activity we can't pause — a download already mid-flight or live
            // streams — still uses connections; capture it so we can flag it.
            var streamsActive = activeReads.Count;
            var (inProgressItem, _) = queueManager.GetInProgressQueueItem();

            var result = await benchmarkService.RunAsync(
                request.ToConnectionDetails(),
                request.MaxConnections,
                request.Intensity,
                request.PipeliningOnly,
                HttpContext.RequestAborted
            ).ConfigureAwait(false);

            if (inProgressItem != null || streamsActive > 0)
            {
                var bits = new List<string>();
                if (inProgressItem != null) bits.Add("a download was still finishing");
                if (streamsActive > 0) bits.Add($"{streamsActive} active stream{(streamsActive == 1 ? "" : "s")}");
                result.Warnings.Insert(0,
                    $"Heads up: {string.Join(" and ", bits)} during the test — those connections couldn't be paused, " +
                    "so the numbers may read a little low. Re-run when fully idle for the cleanest result.");
            }

            return new BenchmarkUsenetConnectionResponse { Status = true, Result = result };
        }
        catch (CouldNotConnectToUsenetException)
        {
            return new BenchmarkUsenetConnectionResponse
            {
                Status = false,
                Error = "Couldn't connect to the provider. Check the host, port and SSL settings."
            };
        }
        catch (CouldNotLoginToUsenetException)
        {
            return new BenchmarkUsenetConnectionResponse
            {
                Status = false,
                Error = "Couldn't log in to the provider. Check the username and password."
            };
        }
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new BenchmarkUsenetConnectionRequest(HttpContext, configManager);
        var response = await BenchmarkAsync(request).ConfigureAwait(false);
        return Ok(response);
    }
}
