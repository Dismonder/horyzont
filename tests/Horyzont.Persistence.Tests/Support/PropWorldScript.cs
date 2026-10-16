using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Persistence.Tests.Support;

/// <summary>The world of the fixture <c>world-v3-props.db</c> (K6-31 D23) and what its tests expect of it.</summary>
internal static class PropWorldScript
{
    /// <summary>A tree harvested in the checkpoint and regrown in the tail.</summary>
    public static readonly PropId Tree = PropId.Create(0, 9, 2, 0);

    /// <summary>A rock harvested in the checkpoint (rocks never regrow).</summary>
    public static readonly PropId Rock = PropId.Create(0, 9, 2, 1);

    /// <summary>A bush harvested in the checkpoint.</summary>
    public static readonly PropId Bush = PropId.Create(2, 300, 410, 0);

    /// <summary>A tree on another face harvested in the tail.</summary>
    public static readonly PropId Late = PropId.Create(3, 700, 12, 0);

    public const ulong TreeTick = 120;
    public const ulong RockTick = 300;
    public const ulong BushTick = 450;
    public const ulong LateTick = 900;

    /// <summary>A fresh world whose planet has the prop layout and whose item types include the yield of the props.</summary>
    public static WorldState NewWorld() => WorldFactory.CreateWithProps(0);

    /// <summary>The checkpoint holds the tree, the rock and the bush; the tail harvests <see cref="Late"/> and regrows <see cref="Tree"/>.</summary>
    public static async Task BuildAsync(WorldHarness h)
    {
        var planet = WorldFactory.HomePlanet;
        h.Sim.ApplyServerOperation(new PropHarvestOp(planet, Tree, new Tick(TreeTick)));
        h.Sim.ApplyServerOperation(new PropHarvestOp(planet, Rock, new Tick(RockTick)));
        h.Sim.ApplyServerOperation(new PropHarvestOp(planet, Bush, new Tick(BushTick)));
        Assert.True(await h.CheckpointAndPump());

        h.Sim.ApplyServerOperation(new PropHarvestOp(planet, Late, new Tick(LateTick)));
        h.Sim.ApplyServerOperation(new PropRegrowOp(planet, Tree));
        await h.FlushAndPump();
    }
}
