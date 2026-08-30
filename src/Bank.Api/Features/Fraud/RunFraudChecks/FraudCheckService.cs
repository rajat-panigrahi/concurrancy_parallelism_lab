namespace Bank.Api.Features.Fraud.RunFraudChecks;

public readonly record struct FraudVerdict(int CheckId, bool Suspicious, int ThreadId);

/// <summary>
/// Stands in for a slow external dependency — a fraud-scoring API, a sanctions list, a
/// credit bureau. The kind of call that dominates a request's latency and uses none of
/// its CPU.
/// </summary>
/// <remarks>
/// The waiting is <see cref="Task.Delay"/>, not <see cref="Thread.Sleep"/>, and the
/// difference is the entire lesson. <c>Task.Delay</c> releases the thread and schedules
/// a continuation; <c>Thread.Sleep</c> keeps the thread and does nothing with it. Ten
/// concurrent <c>Task.Delay</c>s cost roughly one thread; ten <c>Thread.Sleep</c>s cost
/// ten.
/// </remarks>
public sealed class FraudCheckService
{
    public async Task<FraudVerdict> CheckAsync(int checkId, TimeSpan latency, CancellationToken cancellationToken)
    {
        await Task.Delay(latency, cancellationToken);

        return new FraudVerdict(checkId, checkId % 7 == 0, Environment.CurrentManagedThreadId);
    }

    /// <summary>The same call written the blocking way, for the comparison.</summary>
    public FraudVerdict CheckBlocking(int checkId, TimeSpan latency)
    {
        Thread.Sleep(latency);

        return new FraudVerdict(checkId, checkId % 7 == 0, Environment.CurrentManagedThreadId);
    }
}
