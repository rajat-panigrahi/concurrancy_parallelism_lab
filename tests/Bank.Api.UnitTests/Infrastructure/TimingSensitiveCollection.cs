namespace Bank.Api.UnitTests.Infrastructure;

/// <summary>
/// Tests that assert on elapsed time rather than on a value.
/// </summary>
/// <remarks>
/// <para>xUnit runs test collections in parallel. A test measuring "is the parallel
/// version faster?" while three other tests are saturating the same four cores is
/// measuring the test runner, not the code — which is how a suite about race conditions
/// ends up with its own flaky tests.</para>
/// <para>Putting every timing-sensitive class in one collection makes them run one at a
/// time. Their assertions are also deliberately order-of-magnitude ("at least twice as
/// fast"), never precise, because a precise timing assertion on shared CI hardware is a
/// flake waiting to happen. ADR-0016 has the reasoning.</para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingSensitiveCollection
{
    public const string Name = "timing-sensitive";
}
