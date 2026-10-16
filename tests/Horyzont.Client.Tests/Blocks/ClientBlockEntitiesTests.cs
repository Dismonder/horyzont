using System.Numerics;

using Horyzont.Client.Blocks;
using Horyzont.Client.Replication;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Tests.Blocks;

public sealed class ClientBlockEntitiesTests
{
    private static readonly EntityId _construct = new(100);
    private static readonly EntityId _other = new(101);
    private static readonly CellPos _chestCell = new(0, 1, 0);
    private static readonly CellPos _bedCell = new(1, 1, 0);

    private static BlockEntityStates States(EntityId construct, params (CellPos Cell, BlockEntityState State)[] entries) =>
        new(construct, [.. entries.Select(e => new BlockEntityEntry(e.Cell, e.State))]);

    private static ConstructState FullState(EntityId construct) =>
        new(new ConstructHeaderRecord(construct, FrameRef.Of(new PlanetId(1)), 0, ConstructMode.Static, Vector3.Zero, Quaternion.Identity, 1), 0, 1, []);

    [Fact]
    public void States_ReplaceConstructMap()
    {
        var entities = new ClientBlockEntities();
        var changed = new List<EntityId>();
        entities.Changed += changed.Add;

        entities.Apply(States(_construct, (_chestCell, new ChestBlockState(new ContainerId(7))), (_bedCell, new BedBlockState(PlayerId.None))));

        Assert.Equal(2, entities.CountOf(_construct));
        Assert.True(entities.TryGet(_construct, _chestCell, out var chest));
        Assert.Equal(new ChestBlockState(new ContainerId(7)), chest);
        Assert.Equal([_construct], changed);

        // A later message is the whole list: the bed is gone, the chest holds another container.
        entities.Apply(States(_construct, (_chestCell, new ChestBlockState(new ContainerId(8)))));

        Assert.Equal(1, entities.CountOf(_construct));
        Assert.True(entities.TryGet(_construct, _chestCell, out chest));
        Assert.Equal(new ChestBlockState(new ContainerId(8)), chest);
        Assert.False(entities.TryGet(_construct, _bedCell, out _));
        Assert.Equal(0, entities.CountOf(_other));
    }

    [Fact]
    public void EmptyStates_ClearsMap()
    {
        var entities = new ClientBlockEntities();
        entities.Apply(States(_construct, (_chestCell, new ChestBlockState(new ContainerId(7)))));
        entities.Apply(States(_other, (_bedCell, new BedBlockState(new PlayerId(1)))));

        entities.Apply(States(_construct));

        Assert.Equal(0, entities.CountOf(_construct));
        Assert.False(entities.TryGet(_construct, _chestCell, out _));
        Assert.Equal(1, entities.CountOf(_other));
    }

    [Fact]
    public void ConstructRemoved_ClearsMap()
    {
        var entities = new ClientBlockEntities();
        var replication = new ClientReplication(static _ => { }, null, null, entities);
        entities.Apply(States(_construct, (_chestCell, new ChestBlockState(new ContainerId(7)))));
        entities.Apply(States(_other, (_bedCell, new BedBlockState(new PlayerId(1)))));

        replication.OnMessage(new ConstructRemoved(_construct));

        Assert.Equal(0, entities.CountOf(_construct));
        Assert.Equal(1, entities.CountOf(_other));
    }

    [Fact]
    public void NewConstructState_ClearsMap()
    {
        var entities = new ClientBlockEntities();
        var replication = new ClientReplication(static _ => { }, null, null, entities);
        entities.Apply(States(_construct, (_chestCell, new ChestBlockState(new ContainerId(7)))));

        replication.OnMessage(FullState(_construct));

        Assert.Equal(0, entities.CountOf(_construct));
    }

    [Fact]
    public void ReplicationRoutesMessage46_AndDisconnectClears()
    {
        var entities = new ClientBlockEntities();
        var replication = new ClientReplication(static _ => { }, null, null, entities);

        replication.OnMessage(States(_construct, (_chestCell, new ChestBlockState(new ContainerId(7)))));
        Assert.Equal(1, entities.CountOf(_construct));

        replication.OnDisconnected(default);
        Assert.Equal(0, entities.CountOf(_construct));
    }

    [Fact]
    public void StatesAfterTheFullState_AreKept()
    {
        var entities = new ClientBlockEntities();
        var replication = new ClientReplication(static _ => { }, null, null, entities);

        replication.OnMessage(FullState(_construct));
        replication.OnMessage(States(_construct, (_chestCell, new ChestBlockState(new ContainerId(7)))));

        Assert.Equal(1, entities.CountOf(_construct));
    }
}
