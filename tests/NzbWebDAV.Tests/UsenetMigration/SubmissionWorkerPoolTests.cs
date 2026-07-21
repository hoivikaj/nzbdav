using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;
using NzbWebDAV.Queue;
using NzbWebDAV.Services;
using NzbWebDAV.Services.Metrics;
using NzbWebDAV.Services.StreamTrace;
using NzbWebDAV.UsenetMigration.Runner;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Tests.UsenetMigration;

public sealed class SubmissionWorkerPoolTests
{
    [Fact]
    public async Task SubmitBatch_CancelledAfterFirstSubmission_DoesNotSubmitNextPendingRelease()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedPendingAsync(h, "store-a", "store-b");
        using var queueManager = CreateQueueManager();
        using var submission = new CancellationTokenSource();
        var submitCalls = 0;
        var pool = CreatePool(h, queueManager);
        pool.SubmitPreparedReleaseOverride = (_, _, _, _) =>
        {
            submitCalls++;
            submission.Cancel();
            return Task.CompletedTask;
        };

        var submitted = await pool.SubmitBatchAsync(submission.Token);

        Assert.Equal(1, submitted);
        Assert.Equal(1, submitCalls);
        await using var check = h.Mig();
        var states = await check.Submissions
            .OrderBy(s => s.StoreRef)
            .Select(s => s.State)
            .ToListAsync();
        Assert.Equal(["submitted", "pending"], states);
    }

    [Fact]
    public async Task SubmitBatch_CrashAfterQueueCommit_RecoversClaimWithoutResubmitting()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedPendingAsync(h, "store-a");
        using var queueManager = CreateQueueManager();
        var submitCalls = 0;
        Guid committedId = default;
        var pool = CreatePool(h, queueManager);
        pool.SubmitPreparedReleaseOverride = async (release, claimedId, _, _) =>
        {
            submitCalls++;
            committedId = claimedId;
            await using var dav = h.Dav();
            dav.QueueItems.Add(new QueueItem
            {
                Id = claimedId,
                CreatedAt = DateTime.UtcNow,
                FileName = release.QueueFileName,
                JobName = release.JobName,
                NzbFileSize = 100,
                TotalSegmentBytes = 100,
                Category = release.TargetCategory!,
                Priority = QueueItem.PriorityOption.Low,
                PostProcessing = QueueItem.PostProcessingOption.None,
            });
            await dav.SaveChangesAsync();
            throw new IOException("simulated process loss after AddFile committed");
        };

        Assert.Equal(0, await pool.SubmitBatchAsync(CancellationToken.None));
        await using (var afterCrash = h.Mig())
        {
            var claimed = await afterCrash.Submissions.SingleAsync();
            Assert.Equal("submitting", claimed.State);
            Assert.Equal(committedId.ToString(), claimed.NzoId);
        }

        Assert.Equal(0, await pool.SubmitBatchAsync(CancellationToken.None));

        Assert.Equal(1, submitCalls);
        await using (var recovered = h.Mig())
        {
            var adopted = await recovered.Submissions.SingleAsync();
            Assert.Equal("submitted", adopted.State);
            Assert.Equal(committedId.ToString(), adopted.NzoId);
        }
        await using (var dav = h.Dav())
            Assert.Equal(committedId, (await dav.QueueItems.SingleAsync()).Id);
    }

    private static SubmissionWorkerPool CreatePool(MigrationTestHarness h, QueueManager queueManager)
    {
        var pool = new SubmissionWorkerPool(
            h.Store,
            queueManager,
            new ConfigManager(),
            new WebsocketManager())
        {
            DavContextFactory = h.DavFactory,
            BuildNzbOverride = (_, _) => Task.FromResult<byte[]>([1]),
        };
        return pool;
    }

    private static async Task SeedPendingAsync(MigrationTestHarness h, params string[] storeRefs)
    {
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "running";
            s.MaxQueueDepth = 10;
        });
        await using var migration = h.Mig();
        foreach (var storeRef in storeRefs)
        {
            migration.Releases.Add(new MigrationRelease
            {
                StoreRef = storeRef,
                StoreBasename = storeRef,
                SubmitFileName = $"{storeRef}.nzb",
                QueueFileName = $"{storeRef}.nzb",
                JobName = storeRef,
                TargetCategory = "tv",
                Verdict = "green",
                VerdictReasons = "[]",
                ScannedAt = DateTime.UtcNow,
            });
            migration.Submissions.Add(new MigrationSubmission
            {
                StoreRef = storeRef,
                State = "pending",
                UpdatedAt = DateTime.UtcNow,
            });
        }
        await migration.SaveChangesAsync();
    }

    private static QueueManager CreateQueueManager()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.UsenetProviders,
                ConfigValue = JsonSerializer.Serialize(new UsenetProviderConfig()),
            },
        ]);
        var websocket = new WebsocketManager();
        var usenet = new UsenetStreamingClient(
            config,
            websocket,
            new ProviderUsageTracker(),
            new MetricsWriter(),
            new ProviderBytesTracker(),
            new StreamTraceBuffer(100),
            new ActiveReadRegistry());
        return new QueueManager(
            usenet,
            config,
            websocket,
            new ProviderUsageTracker(),
            new WatchdogLog(),
            new QueueItemSourceTracker(),
            new BenchmarkGate(),
            startLoop: false);
    }
}
