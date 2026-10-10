using System.Globalization;
using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Console;
using Horyzont.Server.Frames;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Frames;

public sealed class FrameConsoleTests
{
    [Fact]
    public async Task FrameCommand_ListAndReparent_WithDevCommands()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        await rig.JoinAsync("local", 1);
        var state = rig.Runtime.State;
        var graph = new FrameGraph(new WorldStateFramePoses(state, new HomeOnlyPlacement(WorldFactory.HomePlanet)));
        var history = new ServerFrameHistory(state, graph);
        var transitions = new FrameTransitions(rig.Runtime.Simulation, history);
        var processor = new ConsoleCommandProcessor(rig.Runtime, NullLogger<ConsoleCommandProcessor>.Instance, devCommands: true, frames: () => transitions);

        state.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone);
        var construct = state.EntityIds.PeekNext();
        rig.Runtime.Simulation.ApplyServerOperation(new ConstructCreateOp(
            construct, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0f, 4010f, 0f), Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(stone, 0)));
        var player = Assert.Single(state.Players.Online);

        var list = processor.Execute("frame list").Split('\n');
        Assert.Contains(list, l => l.StartsWith(Invariant($"FRAME construct {construct.Value} parent=planet:1 seq=0 mode=Static world="), StringComparison.Ordinal));
        Assert.Contains(list, l => l.StartsWith(Invariant($"FRAME player {player.Player.Value} entity={player.Entity.Value} parent=planet:1 seq=0 seat=- world="), StringComparison.Ordinal));

        var reply = processor.Execute(Invariant($"frame reparent {player.Entity.Value} construct:{construct.Value} seat 0 0 0"));
        Assert.StartsWith(Invariant($"FRAME reparent entity={player.Entity.Value} parent=construct:{construct.Value} seq=1 op="), reply, StringComparison.Ordinal);
        Assert.Equal(FrameRef.Of(construct), player.Frame);
        Assert.Equal(new CellPos(0, 0, 0), player.Seat);

        Assert.Equal(Invariant($"FRAME refused entity={player.Entity.Value} reason=PlayerToSystem"), processor.Execute(Invariant($"frame reparent {player.Entity.Value} system")));
        Assert.Equal(Invariant($"FRAME refused entity={construct.Value} reason=ConstructNotShip"), processor.Execute(Invariant($"frame reparent {construct.Value} planet:1")));
        Assert.StartsWith(Invariant($"FRAME reparent entity={player.Entity.Value} parent=planet:1 seq=2 op="), processor.Execute(Invariant($"frame reparent {player.Entity.Value} planet:1")), StringComparison.Ordinal);
        Assert.StartsWith("usage: frame", processor.Execute("frame reparent x planet:1"), StringComparison.Ordinal);
        Assert.StartsWith("usage: frame", processor.Execute("frame reparent 1 moon:1"), StringComparison.Ordinal);
        Assert.StartsWith("usage: frame", processor.Execute("frame"), StringComparison.Ordinal);

        // Without --dev the command does not exist.
        var plain = new ConsoleCommandProcessor(rig.Runtime, NullLogger<ConsoleCommandProcessor>.Instance, frames: () => transitions);
        Assert.StartsWith("commands:", plain.Execute("frame list"), StringComparison.Ordinal);
        await rig.StopAsync();
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
