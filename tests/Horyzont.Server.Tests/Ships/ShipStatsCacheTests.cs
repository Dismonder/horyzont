using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Ships;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Ships;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Ships;

public sealed class ShipStatsCacheTests
{
    private static readonly EntityId _ship = ShipFixtures.ConstructId;

    private static (ServerSimulation Sim, InMemoryJournal Journal, ShipStatsCache Cache) Create()
    {
        var state = ShipFixtures.CreateWorld(null);
        var journal = new InMemoryJournal();
        var sim = new ServerSimulation(state, new FakeTerrainService(), journal, new GameRules(), NullLogger<ServerSimulation>.Instance, ShipFixtures.ContentFor(state));
        foreach (var op in ShipFixtures.BuildOperations(_ship, ShipFixtures.Thirty, new Vector3(0, 4000, 0)))
        {
            sim.ApplyServerOperation(op);
        }

        return (sim, journal, new ShipStatsCache(sim));
    }

    private static ShipModeSetOp SetMode(ServerSimulation sim, ConstructMode mode)
    {
        sim.State.Constructs.TryGet(_ship, out var construct);
        return new ShipModeSetOp(_ship, mode, construct!.Parent, construct.ParentSeq, construct.Origin, construct.Rotation);
    }

    [Fact]
    public void ShipStatsCache_RecomputesOnModeAndBlockChanges()
    {
        var (sim, _, cache) = Create();
        Assert.False(cache.TryGet(_ship, out _));
        Assert.Equal(0, cache.Count);

        sim.ApplyServerOperation(SetMode(sim, ConstructMode.Ship));
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(_ship, out var first));
        Assert.Equal(8210f, first.MassKg);
        Assert.Same(first, GetAgain(cache));

        // One more hull block: +200 kg; removing it again restores the mass.
        var extra = new CellPos(5, 1, 1);
        sim.ApplyServerOperation(new BlockSetOp(_ship, extra, ShipFixtures.StateOf(new ShipBlock(ShipFixtures.Hull, extra))));
        Assert.True(cache.TryGet(_ship, out var more));
        Assert.Equal(8410f, more.MassKg);
        Assert.Equal(31, more.BlockCount);

        sim.ApplyServerOperation(new BlockClearOp(_ship, extra));
        Assert.True(cache.TryGet(_ship, out var back));
        Assert.Equal(8210f, back.MassKg);

        // A second engine block changes the thrust in the matching direction.
        var engine = new CellPos(5, 2, 2);
        sim.ApplyServerOperation(new BlockSetOp(_ship, engine, ShipFixtures.StateOf(new ShipBlock(ShipFixtures.Engine, engine, ShipFixtures.EngineFacing(Face.NegX)))));
        Assert.True(cache.TryGet(_ship, out var thrust));
        Assert.Equal(60000f, thrust.Thrust.PosX);
        Assert.Equal(5, thrust.Engines);

        sim.ApplyServerOperation(SetMode(sim, ConstructMode.Static));
        Assert.False(cache.TryGet(_ship, out _));
        Assert.Equal(0, cache.Count);
    }

    private static ShipStats GetAgain(ShipStatsCache cache)
    {
        Assert.True(cache.TryGet(_ship, out var stats));
        return stats;
    }

    [Fact]
    public void ShipStatsCache_AfterReplay_ComputesLazilyFromTheState()
    {
        var (live, journal, _) = Create();
        live.ApplyServerOperation(SetMode(live, ConstructMode.Ship));
        var freshState = ShipFixtures.CreateWorld(null);
        var fresh = new ServerSimulation(
            freshState, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance, ShipFixtures.ContentFor(freshState));
        var cache = new ShipStatsCache(fresh);

        foreach (var entry in journal.Entries)
        {
            fresh.Replay(entry);
        }

        // Replay raises no events, yet the cache answers from the replayed state.
        Assert.Equal(0, cache.Count);
        Assert.True(cache.TryGet(_ship, out var stats));
        Assert.Equal(8210f, stats.MassKg);
    }

    [Fact]
    public void ShipStatsCache_ConstructRemoved_DropsTheEntry()
    {
        var (sim, _, cache) = Create();
        sim.ApplyServerOperation(SetMode(sim, ConstructMode.Ship));
        Assert.Equal(1, cache.Count);

        // Clear all 30 blocks (the last one removes the construct); nobody sits in the ship.
        foreach (var block in ShipFixtures.Thirty)
        {
            sim.ApplyServerOperation(new BlockClearOp(_ship, block.Cell));
        }

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(_ship, out _));
    }
}
