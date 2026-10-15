using Horyzont.Testing;

namespace Horyzont.Server.Tests;

public sealed class AllocationProbeTests
{
    [Fact]
    public void MinAllocatedBytes_AllocatingBody_ReportsTheAllocationOfEveryRound()
    {
        var sink = new object[1];

        var allocated = AllocationProbe.MinAllocatedBytes(() => sink[0] = new byte[100]);

        Assert.True(allocated >= 100, $"allocated {allocated} bytes");
    }

    [Fact]
    public void MinAllocatedBytes_AllocationFreeBody_ReportsZero()
    {
        var values = new int[16];
        var sum = 0;

        var allocated = AllocationProbe.MinAllocatedBytes(() =>
        {
            for (var i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }
        });

        Assert.Equal(0, allocated);
        GC.KeepAlive(sum);
    }

    [Fact]
    public void MinAllocatedBytes_NoRounds_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AllocationProbe.MinAllocatedBytes(() => { }, rounds: 0));
    }
}
