using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Props;

/// <summary>
/// The entity replication leaves the props to message 45 (K6-31 stage C, step C4; spec: "OperationApplied bez przedmiotów i obiektów"): a
/// compound of harvests, regrowths and terrain edits is not a construct operation, so no client gets an <see cref="OperationApplied"/> for it.
/// </summary>
public sealed class PropEntityReplicationTests
{
    private static readonly PropId _tree = PropId.Create(0, 9, 2, 0);
    private static readonly PropId _bush = PropId.Create(0, 9, 3, 0);

    [Fact]
    public async Task PropCompounds_SendNoOperationApplied_ToClients()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var alice = await rig.JoinAsync("alice", 1, new Vector3(5f, 4000f, 0f));
        var construct = rig.AddConstruct(new Vector3(0, 4000, 0));
        rig.Step(60);
        Assert.True(rig.Replication.Constructs.IsLive(alice.Player, construct));
        var sim = rig.Runtime.Simulation;
        var planet = WorldFactory.HomePlanet;

        // The twin: a block put on the construct reaches the client as an operation.
        alice.Received.Clear();
        sim.ApplyServerOperation(new BlockSetOp(construct, new CellPos(1, 0, 0), ReplicationRig.Stone));
        rig.Step(3);
        Assert.Single(alice.Received.OfType<OperationApplied>());

        // A harvest, a harvest with a dig, and a regrowth in a compound: none of them is.
        alice.Received.Clear();
        sim.ApplyServerOperation(new PropHarvestOp(planet, _tree, sim.State.CurrentTick));
        sim.ApplyServerOperation(new CompoundOp(
        [
            new PropHarvestOp(planet, _bush, sim.State.CurrentTick),
            new TerrainEditOp(planet, TerrainShape.Sphere, new Vector3(0, 4000, 0), 1f, TerrainEditMode.Remove, 0),
        ]));
        sim.ApplyServerOperation(new CompoundOp([new PropRegrowOp(planet, _tree), new PropRegrowOp(planet, _bush)]));
        rig.Step(3);

        Assert.DoesNotContain(alice.Received, m => m is OperationApplied);
    }
}
