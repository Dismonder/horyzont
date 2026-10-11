using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Server.Characters;
using Horyzont.Game.Server.Replication;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Server.Replication;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Support;

/// <summary>Characters that the test places by hand.</summary>
internal sealed class FakeCharacters : IServerCharacterSource
{
    public Dictionary<PlayerId, ServerCharacterState> States { get; } = [];

    public bool TryGetState(PlayerId player, out ServerCharacterState state) => States.TryGetValue(player, out state);
}

/// <summary>Snapshot budget that records which players were forgotten.</summary>
internal sealed class RecordingSnapshotBudget : ISnapshotBudget
{
    public List<PlayerId> Forgotten { get; } = [];

    public int GetByteBudget(PlayerId player, Tick tick) => 1150;

    public void Debit(PlayerId player, int encodedBytes)
    {
    }

    public void Forget(PlayerId player) => Forgotten.Add(player);
}

/// <summary>Reliable budget the test opens and closes: Live traffic always passes, Bulk only while <see cref="Open"/>.</summary>
internal sealed class GatedSendBudget : IReplicationSendBudget
{
    public List<PlayerId> Forgotten { get; } = [];

    public void Forget(PlayerId player) => Forgotten.Add(player);

    public bool Open { get; set; } = true;

    public long BulkBytes { get; private set; }

    public long LiveBytes { get; private set; }

    public bool TryConsume(PlayerId player, ReplicationTraffic traffic, int bytes)
    {
        if (traffic == ReplicationTraffic.Live)
        {
            LiveBytes += bytes;
            return true;
        }

        if (!Open)
        {
            return false;
        }

        BulkBytes += bytes;
        return true;
    }
}

/// <summary>A <see cref="ServerRig"/> with the entity replication bound to hand-placed characters.</summary>
internal sealed class ReplicationRig : IAsyncDisposable
{
    public static readonly BlockState Stone = BlockState.Create(1, 0);

    private ReplicationRig(ServerRig rig, FakeCharacters characters, EntityReplicationBinder binder)
    {
        Rig = rig;
        Characters = characters;
        Binder = binder;
    }

    public ServerRig Rig { get; }

    public FakeCharacters Characters { get; }

    public EntityReplicationBinder Binder { get; }

    public ServerRuntime Runtime => Rig.Runtime;

    public EntityReplication Replication => Binder.Replication ?? throw new InvalidOperationException("The replication does not exist yet.");

    public FakeTimeProvider Time => Rig.Time;

    public static async Task<ReplicationRig> StartAsync(
        Func<WorldState>? createWorld = null, ISnapshotBudget? snapshotBudget = null, FakeTimeProvider? time = null,
        IReplicationSendBudget? sendBudget = null, LinkConditions? conditions = null,
        Func<ServerRuntimeOptions, ServerRuntimeOptions>? configure = null)
    {
        var rig = new ServerRig(time: time, createWorld: createWorld, conditions: conditions, configure: configure);
        var characters = new FakeCharacters();
        var binder = new EntityReplicationBinder(
            runtime => new ServerReplicationSource(runtime.State, characters), rig.Time, rig.Log, snapshotBudget, 500f, sendBudget);
        binder.Register(rig.Runtime);
        await rig.StartAsync();
        return new ReplicationRig(rig, characters, binder);
    }

    public async Task<RigClient> JoinAsync(string name, ulong identity, Vector3? at = null, PlanetId? planet = null)
    {
        var client = await Rig.JoinAsync(name, identity);
        Place(client.Player, at ?? new Vector3(0, 4000, 0), planet);
        return client;
    }

    public void Place(PlayerId player, Vector3 feet, PlanetId? planet = null, uint lastInput = 0)
    {
        var id = planet ?? WorldFactory.HomePlanet;
        Characters.States[player] = new ServerCharacterState(
            id, FrameRef.Of(id), 0, feet, Quaternion.Identity, 0, Vector3.Zero, MovementMode.Walking, lastInput);
    }

    /// <summary>Places a character in any frame (a construct frame for K5-20), its body still on the home planet.</summary>
    public void PlaceIn(PlayerId player, FrameRef frame, Vector3 local, uint parentSeq = 0) =>
        Characters.States[player] = new ServerCharacterState(
            WorldFactory.HomePlanet, frame, parentSeq, local, Quaternion.Identity, 0, Vector3.Zero, MovementMode.Walking, 0);

    public EntityId EntityOf(PlayerId player) =>
        Runtime.State.Players.TryGet(player, out var state) ? state.Entity : throw new InvalidOperationException("Unknown player.");

    public EntityId AddConstruct(Vector3 origin, params CellPos[] extraCells)
    {
        var id = Runtime.State.EntityIds.PeekNext();
        Runtime.Simulation.ApplyServerOperation(new ConstructCreateOp(
            id, FrameRef.Of(WorldFactory.HomePlanet), origin, Quaternion.Identity, new CellPos(0, 0, 0), Stone));
        foreach (var cell in extraCells)
        {
            Runtime.Simulation.ApplyServerOperation(new BlockSetOp(id, cell, Stone));
        }

        return id;
    }

    /// <summary>
    /// Creates a one-block construct and switches it to ship mode in place with a <c>ShipModeSetOp</c> (a runtime starts only from
    /// a fresh world, so a ship header cannot be loaded).
    /// </summary>
    public EntityId AddShip(Vector3 origin, Quaternion? rotation = null)
    {
        var id = Runtime.State.EntityIds.PeekNext();
        Runtime.Simulation.ApplyServerOperation(new ConstructCreateOp(
            id, FrameRef.Of(WorldFactory.HomePlanet), origin, rotation ?? Quaternion.Identity, new CellPos(0, 0, 0), Stone));
        SetMode(id, ConstructMode.Ship);
        return id;
    }

    public void SetMode(EntityId construct, ConstructMode mode) =>
        Runtime.Simulation.ApplyServerOperation(TestShips.ModeOp(
            Runtime.State.Constructs.TryGet(construct, out var found) ? found! : throw new InvalidOperationException("Unknown construct."), mode));

    public void Step(int frames = 1) => Rig.StepMany(frames);

    public ValueTask DisposeAsync() => Rig.DisposeAsync();
}
