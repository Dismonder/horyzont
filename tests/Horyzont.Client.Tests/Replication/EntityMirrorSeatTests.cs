using System.Buffers.Binary;
using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Replication;

/// <summary>K5-26 decision 7: the seat of a player from its spawn (<c>KindData</c>) and from live reparents.</summary>
public sealed class EntityMirrorSeatTests
{
    private static readonly EntityId _player = new(9);
    private static readonly EntityId _ship = new(20);
    private static readonly FrameRef _home = FrameRef.Of(new PlanetId(1));

    private static EntityState State(uint seq = 0) =>
        new(_player, (EntityStateFields)15, 0, seq, new Vector3(1, 2, 3), Quaternion.Identity, Vector3.Zero, 0);

    private static byte[] Seat(int version, int x, int y, int z)
    {
        var data = new byte[13];
        data[0] = (byte)version;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(1), x);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(5), y);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(9), z);
        return data;
    }

    private static EntitySpawn Spawn(FrameRef frame, byte[] kindData, EntityKind kind = EntityKind.Player) =>
        new(_player, kind, 0, kind == EntityKind.Player ? new PlayerId(1) : null, new SnapshotFrame(frame, 0), State(), kindData);

    [Fact]
    public void EntitySpawn_SeatEncoding_IsVersionAndThreeLittleEndianInts()
    {
        var data = EntitySpawnSeat.Encode(new CellPos(1, -2, 300));
        Assert.Equal(Seat(1, 1, -2, 300), data);
        Assert.Empty(EntitySpawnSeat.Encode(null));
    }

    [Fact]
    public void EntityMirror_Seat_FromSpawnAndReparent()
    {
        var mirror = new EntityMirror(new FakeTimeProvider());
        mirror.Apply(Spawn(FrameRef.Of(_ship), Seat(1, 1, 2, 1)));
        Assert.True(mirror.TryGetSeat(_player, out var seat));
        Assert.Equal(new CellPos(1, 2, 1), seat);

        // A live reparent to another seat of the same ship.
        mirror.Apply(new OperationApplied(new OpId(1), new Tick(10),
            new EntityReparentOp(_player, FrameRef.Of(_ship), 1, Vector3.One, Quaternion.Identity, Vector3.Zero, new CellPos(3, 2, 1)), []));
        Assert.True(mirror.TryGetSeat(_player, out seat));
        Assert.Equal(new CellPos(3, 2, 1), seat);

        // Out of the ship: no seat; a stale reparent (old sequence) changes nothing.
        mirror.Apply(new OperationApplied(new OpId(2), new Tick(11),
            new EntityReparentOp(_player, _home, 2, Vector3.One, Quaternion.Identity, Vector3.Zero, null), []));
        Assert.False(mirror.TryGetSeat(_player, out _));
        mirror.Apply(new OperationApplied(new OpId(3), new Tick(12),
            new EntityReparentOp(_player, FrameRef.Of(_ship), 1, Vector3.One, Quaternion.Identity, Vector3.Zero, new CellPos(0, 0, 0)), []));
        Assert.False(mirror.TryGetSeat(_player, out _));

        // A spawn without a seat, then a despawn, forget it.
        mirror.Apply(Spawn(FrameRef.Of(_ship), Seat(1, 4, 4, 4)));
        Assert.True(mirror.TryGetSeat(_player, out _));
        mirror.Apply(Spawn(_home, []));
        Assert.False(mirror.TryGetSeat(_player, out _));
        mirror.Apply(Spawn(FrameRef.Of(_ship), Seat(1, 4, 4, 4)));
        mirror.Apply(new EntityDespawn(_player));
        Assert.False(mirror.TryGetSeat(_player, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(64)]
    public void EntityMirror_PlayerKindData_RejectsMalformedLength(int length)
    {
        var mirror = new EntityMirror(new FakeTimeProvider());
        var data = new byte[length];
        data[0] = 1;
        Assert.Throws<ProtocolViolationException>(() => mirror.Apply(Spawn(FrameRef.Of(_ship), data)));
        Assert.False(mirror.TryGetKind(_player, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(255)]
    public void EntityMirror_PlayerKindData_RejectsUnknownVersion(int version)
    {
        var mirror = new EntityMirror(new FakeTimeProvider());
        Assert.Throws<ProtocolViolationException>(() => mirror.Apply(Spawn(FrameRef.Of(_ship), Seat(version, 1, 2, 1))));
    }

    [Fact]
    public void EntityMirror_PlayerKindData_RejectsSeatOutsideConstructOrBounds()
    {
        var mirror = new EntityMirror(new FakeTimeProvider());
        Assert.Throws<ProtocolViolationException>(() => mirror.Apply(Spawn(_home, Seat(1, 1, 2, 1))));
        Assert.Throws<ProtocolViolationException>(() => mirror.Apply(Spawn(FrameRef.Of(_ship), Seat(1, int.MaxValue, 0, 0))));

        // Constructs keep their (empty) kind data without a meaning.
        mirror.Apply(Spawn(FrameRef.Of(_ship), [], EntityKind.Construct));
        Assert.False(mirror.TryGetSeat(_player, out _));
    }
}
