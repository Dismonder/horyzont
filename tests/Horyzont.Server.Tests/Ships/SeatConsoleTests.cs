using System.Globalization;
using System.Numerics;

using Horyzont.Core.Intents;
using Horyzont.Server.Console;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-25 decision 22: the dev console command <c>seat</c> goes through the intent validators.</summary>
public sealed class SeatConsoleTests
{
    [Fact]
    public void SeatCommand_EnterExitEvict_ThroughValidators()
    {
        var rig = new SeatRig(guard: false);
        var player = rig.Join(1);
        var other = rig.Join(2);
        rig.StandAt(player, new Vector3(2.5f, 0f, -2f));
        var sim = rig.Flight.Sim;
        var ship = FlightRig.Ship.Value;
        string Run(string line) => SeatCommands.Execute(sim, line.Split(' '));

        Assert.Equal(Line(1, true, RejectReason.None), Run(Invariant($"seat 1 enter {ship} 2 2 0")));
        Assert.Equal(SeatRig.Cockpit, rig.Player(player).Seat);
        Assert.Equal(Line(2, false, RejectReason.SeatOccupied), Run(Invariant($"seat 2 enter {ship} 2 2 0")));
        Assert.Equal(Line(2, false, RejectReason.InvalidParameters), Run("seat 2 exit"));

        Assert.Equal(Line(1, true, RejectReason.None), Run("seat 1 exit"));
        Assert.Null(rig.Player(player).Seat);

        rig.Sit(other, SeatRig.Cockpit);
        rig.Leave(other);
        rig.StandAt(player, new Vector3(2.5f, 0f, -2f));
        Assert.Equal(Line(1, true, RejectReason.None), Run(Invariant($"seat 1 evict {ship} 2 2 0")));
        Assert.Null(rig.Player(other).Seat);

        Assert.Equal(SeatCommands.Usage, Run("seat 1 enter x 2 2 0"));
        Assert.Equal(SeatCommands.Usage, Run("seat 1 dance"));
        Assert.Equal("unknown player 9", Run("seat 9 exit"));
    }

    private static string Line(int player, bool accepted, RejectReason reason) =>
        Invariant($"SEAT player={player} result={(accepted ? "accepted" : "rejected")} code={(int)reason}");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
