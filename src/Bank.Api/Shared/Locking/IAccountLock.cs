namespace Bank.Api.Shared.Locking;

/// <summary>How long an actor queued before it got the lock.</summary>
public interface IAccountLockHandle : IAsyncDisposable
{
    double WaitedMs { get; }
}

/// <summary>
/// Serialises work on one account, so only one actor at a time may run its
/// read-decide-write sequence.
/// </summary>
/// <remarks>
/// <para>There are two implementations, and the difference between them is the whole
/// of the scaling lesson:</para>
/// <list type="bullet">
/// <item><b>In-process</b> — a semaphore per account, living in this process's memory.
/// Correct while there is exactly one process. Add a second instance and each has its
/// own semaphore, so both can enter the "critical section" at once and the guarantee
/// silently evaporates.</item>
/// <item><b>Postgres advisory</b> — the lock lives in the database, which every
/// instance shares, so it keeps working at any number of replicas.</item>
/// </list>
/// <para>Same interface, same call site, completely different scaling behaviour. That
/// is why "can it scale?" is an application-design question before it is an
/// infrastructure question. See ADR-0012 and lessons/09-scaling.md.</para>
/// </remarks>
public interface IAccountLock
{
    /// <summary>Human-readable name for the UI, e.g. <c>in-process semaphore</c>.</summary>
    string Kind { get; }

    /// <summary>True if this lock coordinates across processes.</summary>
    bool WorksAcrossInstances { get; }

    Task<IAccountLockHandle> AcquireAsync(Guid accountId, CancellationToken cancellationToken = default);
}
