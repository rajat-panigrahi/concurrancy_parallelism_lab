namespace Bank.Api.Shared.Contention;

/// <summary>
/// Keeps the call sites in handlers to one readable line, so the recording never
/// obscures the code it is recording.
/// </summary>
public static class ContentionRecorderExtensions
{
    public static void Record(
        this IContentionRecorder recorder,
        Guid runId,
        string actorId,
        Guid accountId,
        ContentionPhase phase,
        int attempt = 1,
        decimal? balanceSeen = null,
        long? versionSeen = null,
        long? versionWritten = null,
        decimal? amount = null,
        double? waitedMs = null,
        string? note = null)
    {
        recorder.Record(new ContentionEvent
        {
            RunId = runId,
            ActorId = actorId,
            AccountId = accountId,
            Sequence = recorder.NextSequence(runId),
            Phase = phase,
            ElapsedMs = recorder.ElapsedMs(runId),
            Attempt = attempt,
            BalanceSeen = balanceSeen,
            VersionSeen = versionSeen,
            VersionWritten = versionWritten,
            Amount = amount,
            WaitedMs = waitedMs,
            Note = note,
        });
    }
}
