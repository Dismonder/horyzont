using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>K6-33, D5: <c>ServerSimulation.UpdatePlayerVitals</c> stores health and stamina outside the journal, like <c>UpdatePlayerKinematics</c>.</summary>
public sealed class PlayerVitalsUpdateTests
{
    private static readonly PlayerVitals _valid = new(80f, 55.5f, new Tick(36000), false);

    [Fact]
    public void UpdatePlayerVitals_Valid_Stored()
    {
        var f = new SimFixture();
        var player = f.Join();
        Assert.Null(f.PlayerOf(player).Vitals);

        Assert.True(f.Sim.UpdatePlayerVitals(player, _valid));
        Assert.Equal(_valid, f.PlayerOf(player).Vitals);

        // A second call replaces the first; the largest legal values and the exhausted flag are legal.
        var other = new PlayerVitals(10_000f, 0f, new Tick(ulong.MaxValue), true);
        Assert.True(f.Sim.UpdatePlayerVitals(player, other));
        Assert.Equal(other, f.PlayerOf(player).Vitals);
    }

    [Fact]
    public void UpdatePlayerVitals_OfflineOrUnknown_False()
    {
        var f = new SimFixture();
        var player = f.Join();
        Assert.True(f.Sim.UpdatePlayerVitals(player, _valid));

        Assert.False(f.Sim.UpdatePlayerVitals(new PlayerId(99), _valid));
        Assert.False(f.Sim.UpdatePlayerVitals(PlayerId.None, _valid));

        var state = f.PlayerOf(player);
        f.Sim.ApplyServerOperation(new PlayerLeaveOp(player, state.Frame, state.ParentSeq, state.Position, Quaternion.Identity, 0f));
        Assert.False(f.Sim.UpdatePlayerVitals(player, new PlayerVitals(1f, 1f, default, false)));

        // The vitals of the player who left stay as they were (the checkpoint saves them).
        Assert.Equal(_valid, f.PlayerOf(player).Vitals);
    }

    [Fact]
    public void UpdatePlayerVitals_NaN_FalseAndKept()
    {
        var f = new SimFixture();
        var player = f.Join();
        Assert.True(f.Sim.UpdatePlayerVitals(player, _valid));

        foreach (var invalid in new[]
        {
            new PlayerVitals(float.NaN, 10f, default, false),
            new PlayerVitals(10f, float.NaN, default, false),
            new PlayerVitals(float.PositiveInfinity, 10f, default, false),
            new PlayerVitals(10f, float.NegativeInfinity, default, false),
            new PlayerVitals(-0.5f, 10f, default, false),
            new PlayerVitals(10f, -0.5f, default, false),
            new PlayerVitals(10_000.5f, 10f, default, false),
            new PlayerVitals(10f, 10_000.5f, default, false),
        })
        {
            Assert.False(f.Sim.UpdatePlayerVitals(player, invalid));
            Assert.Equal(_valid, f.PlayerOf(player).Vitals);
        }
    }

    [Fact]
    public void UpdatePlayerVitals_NotJournaled()
    {
        var f = new SimFixture();
        var player = f.Join();
        var hash = StateHasher.Hash(f.State);
        var last = f.State.LastOpId;
        var journaled = f.Journal.Entries.Count;

        Assert.True(f.Sim.UpdatePlayerVitals(player, _valid));

        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(last, f.State.LastOpId);
        Assert.Equal(journaled, f.Journal.Entries.Count);
        Assert.False(f.Sim.IsFaulted);
    }
}
