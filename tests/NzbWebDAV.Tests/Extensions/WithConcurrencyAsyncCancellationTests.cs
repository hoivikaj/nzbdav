using NzbWebDAV.Extensions;

namespace NzbWebDAV.Tests.Extensions;

public class WithConcurrencyAsyncCancellationTests
{
    [Fact]
    public async Task WithConcurrencyAsync_ThrowsWhenTokenAlreadyCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var tasks = Enumerable.Range(0, 5).Select(i => Task.FromResult(i));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in tasks.WithConcurrencyAsync(2, cts.Token).ConfigureAwait(false))
            {
            }
        });
    }

    [Fact]
    public async Task WithConcurrencyAsync_CompletesWithoutToken()
    {
        var tasks = Enumerable.Range(0, 5).Select(i => Task.FromResult(i));

        var results = new List<int>();
        await foreach (var value in tasks.WithConcurrencyAsync(2).ConfigureAwait(false))
            results.Add(value);

        Assert.Equal([0, 1, 2, 3, 4], results.OrderBy(x => x).ToList());
    }
}
