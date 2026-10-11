using System.Globalization;
using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Server.Console;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-25 decisions 8 and 20, spec 02:192: a player saved in a seat comes back in it; an evicted one at the world start.</summary>
public sealed class SeatRejoinTests
{
    [Fact]
    public void Rejoin_SeatedPlayer_SameSeat()
    {
        var rig = new SeatRig(guard: false);
        var player = rig.Join(1);
        rig.Sit(player, SeatRig.SeatA);
        var seated = rig.Player(player);
        var (frame, seq, position) = (seated.Frame, seated.ParentSeq, seated.Position);
        rig.Leave(player);

        Assert.Equal(player, rig.Join(1));

        var back = rig.Player(player);
        Assert.True(back.IsOnline);
        Assert.Equal(frame, back.Frame);
        Assert.Equal(SeatRig.SeatA, back.Seat);
        Assert.Equal(seq, back.ParentSeq);
        Assert.Equal(position, back.Position);
    }

    [Fact]
    public void Rejoin_Evicted_AtWorldStart()
    {
        var rig = new SeatRig(guard: false);
        var offline = rig.Join(1);
        rig.Sit(offline, SeatRig.Cockpit);
        rig.Leave(offline);
        var other = rig.Join(2);
        rig.StandAt(other, new Vector3(2.5f, 0f, -2f));
        var line = SeatCommands.Execute(rig.Flight.Sim, string.Create(CultureInfo.InvariantCulture, $"seat 2 evict {FlightRig.Ship.Value} 2 2 0").Split(' '));
        Assert.EndsWith("result=accepted code=0", line, StringComparison.Ordinal);

        rig.Join(1);

        var back = rig.Player(offline);
        var sim = rig.Flight.Sim;
        Assert.Null(back.Seat);
        Assert.Equal(FrameRef.Of(sim.State.HomePlanet), back.Frame);
        Assert.True(sim.State.Planets.TryGet(sim.State.HomePlanet, out var home));
        Assert.Equal(RespawnPoints.WorldStart(home!, new FakeTerrainService()), back.Position);
    }

    [Fact]
    public void Checkpoint_SeatedOfflinePlayer_SeatSurvivesRestart()
    {
        var rig = new SeatRig(guard: false);
        var player = rig.Join(1);
        rig.Sit(player, SeatRig.Nav);
        rig.Leave(player);
        var record = PlayerRecord.From(rig.Player(player));

        var writer = new ByteWriter();
        StateCodec.WritePlayer(writer, record);
        var reader = new ByteReader(writer.WrittenSpan);
        var restored = StateCodec.ReadPlayer(ref reader);

        Assert.Equal(record, restored);
        Assert.Equal(SeatRig.Nav, restored.Seat);
        Assert.Equal(FrameRef.Of(FlightRig.Ship), restored.Frame);
        Assert.False(restored.IsOnline);
    }
}
