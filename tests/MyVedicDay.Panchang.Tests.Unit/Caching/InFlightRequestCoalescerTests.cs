using MyVedicDay.Caching;

namespace MyVedicDay.Panchang.Tests.Unit.Caching;

public sealed class InFlightRequestCoalescerTests
{
    [Fact]
    public async Task ConcurrentRequestsForSameKey_ShareOneExecution()
    {
        var coalescer = new InFlightRequestCoalescer<string>();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;

        Task<string> Factory()
        {
            Interlocked.Increment(ref executions);
            return completion.Task;
        }

        var first = coalescer.GetOrStartAsync("key", Factory);
        var second = coalescer.GetOrStartAsync("key", Factory);
        completion.SetResult("value");

        Assert.Equal("value", await first);
        Assert.Equal("value", await second);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task CompletedExecution_IsReleased_SoLaterRequestsRunAgain()
    {
        var coalescer = new InFlightRequestCoalescer<int>();
        var executions = 0;

        await coalescer.GetOrStartAsync("key", () => Task.FromResult(Interlocked.Increment(ref executions)));
        await coalescer.GetOrStartAsync("key", () => Task.FromResult(Interlocked.Increment(ref executions)));

        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task FailedExecution_IsReleased_AndNotCached()
    {
        var coalescer = new InFlightRequestCoalescer<int>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coalescer.GetOrStartAsync("key", () => Task.FromException<int>(new InvalidOperationException())));

        Assert.Equal(42, await coalescer.GetOrStartAsync("key", () => Task.FromResult(42)));
    }
}
