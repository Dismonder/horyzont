using Horyzont.Core.Ids;
using Horyzont.Server.Replication;

namespace Horyzont.Server.Tests.Replication;

public sealed class SnapshotSchedulingPolicyTests
{
    [Fact]
    public void Select_TightBudget_PrioritizesOwnAndEventuallySendsFarEntities()
    {
        var candidates = new SnapshotCandidate[8];
        var lastSent = new int[8];
        var selected = new int[8];
        for (var i = 0; i < 8; i++) { candidates[i] = new SnapshotCandidate(new EntityId((ulong)i + 1), i == 0 ? 0 : i * 70, 50, 0, i == 0); }
        for (var round = 1; round <= 100; round++)
        {
            var count = SnapshotSchedulingPolicy.Select(candidates, selected, 150);
            Assert.Equal(3, count);
            Assert.Equal(0, selected[0]);
            for (var i = 0; i < 8; i++)
            {
                var sent = selected.AsSpan(0, count).Contains(i);
                if (sent) { lastSent[i] = round; }
                candidates[i] = candidates[i] with { OmittedRounds = sent ? 0 : candidates[i].OmittedRounds + 1 };
                Assert.True(round - lastSent[i] < 20, $"Entity {i} starved at round {round}");
            }
        }
    }

    [Fact]
    public void Select_EqualPrioritiesAndForcedAge_IsDeterministicWithinBudget()
    {
        SnapshotCandidate[] candidates =
        [
            new(new EntityId(9), 500, 80, 10, false), new(new EntityId(7), 1, 80, 0, false),
            new(new EntityId(1), 0, 80, 0, true), new(new EntityId(8), 500, 80, 12, false),
            new(new EntityId(6), 1, 80, 0, false),
        ];
        Span<int> indices = stackalloc int[5];
        Assert.Equal(4, SnapshotSchedulingPolicy.Select(candidates, indices, 320));
        Assert.Equal(2, indices[0]);
        Assert.Equal(3, indices[1]);
        Assert.Equal(0, indices[2]);
        Assert.Equal(4, indices[3]);
        Assert.Equal(0, SnapshotSchedulingPolicy.Select(candidates, indices, 79));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(22)]
    public void Select_WarmedUp_AllocatesZeroBytes(int size)
    {
        var candidates = new SnapshotCandidate[size];
        var selected = new int[size];
        for (var i = 0; i < size; i++) { candidates[i] = new SnapshotCandidate(new EntityId((ulong)i + 1), 10 * i, 48, i % 14, i == 0); }
        for (var i = 0; i < 10000; i++) { SnapshotSchedulingPolicy.Select(candidates, selected, 1000); }
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) { SnapshotSchedulingPolicy.Select(candidates, selected, 1000); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
    }
}
