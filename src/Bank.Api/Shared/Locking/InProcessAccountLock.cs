using System.Collections.Concurrent;
using System.Diagnostics;

namespace Bank.Api.Shared.Locking;

/// <summary>
/// One <see cref="SemaphoreSlim"/> per account, held in this process's memory.
/// </summary>
/// <remarks>
/// <para><b>Why SemaphoreSlim and not <c>lock</c>?</b> You cannot <c>await</c> inside a
/// <c>lock</c> block — the C# compiler forbids it, because a monitor is owned by a
/// thread and an await may resume on a different one. Any code that locks around I/O
/// therefore needs an async-aware primitive, and <c>SemaphoreSlim(1, 1)</c> is the
/// standard one. This is a very common interview question.</para>
/// <para><b>Why it does not scale.</b> The semaphores live in
/// <see cref="_locks"/> — a field of one object, in one process. Run three replicas and
/// you have three dictionaries holding three separate semaphores for the same account
/// id, each happily granting access to its own instance at the same moment.</para>
/// </remarks>
public sealed class InProcessAccountLock : IAccountLock
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public string Kind => "in-process semaphore";

    public bool WorksAcrossInstances => false;

    public async Task<IAccountLockHandle> AcquireAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));

        var clock = Stopwatch.StartNew();
        await semaphore.WaitAsync(cancellationToken);
        clock.Stop();

        return new Handle(semaphore, clock.Elapsed.TotalMilliseconds);
    }

    private sealed class Handle(SemaphoreSlim semaphore, double waitedMs) : IAccountLockHandle
    {
        private int _released;

        public double WaitedMs { get; } = waitedMs;

        public ValueTask DisposeAsync()
        {
            // Releasing a semaphore twice raises its count above its maximum and
            // quietly lets two actors in at once, so guard against a double dispose.
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
