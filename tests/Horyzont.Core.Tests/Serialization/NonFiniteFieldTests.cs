using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// NaN and infinity must be rejected in every float and Half field. Each case encodes a valid object whose floats are distinctive "sentinel"
/// values, then overwrites one sentinel at a time with NaN, +Infinity and -Infinity and requires <see cref="ProtocolViolationException"/>.
/// </summary>
public sealed class NonFiniteFieldTests
{
    private static readonly Quaternion _q1 = Kit.UnitQuat(0.18f, 0.36f, 0.54f);
    private static readonly Quaternion _q2 = Kit.UnitQuat(0.21f, 0.33f, 0.44f);
    private static readonly FrameRef _frame = new(FrameKind.Planet, 9);

    private sealed record Sample(byte[] Encoded, DecodeFn Decode, float[] Floats, float[] Halves);

    private static float S(int k) => 1000.25f + (0.25f * k);

    private static Vector3 V(int first) => new(S(first), S(first + 1), S(first + 2));

    private static float[] Components(Vector3 v) => [v.X, v.Y, v.Z];

    private static float[] Components(Quaternion q) => [q.X, q.Y, q.Z, q.W];

    private static Sample Op(Operation op, params float[][] floats) =>
        new(Kit.Encode(op), d => Kit.DecodeOperation(d), floats.SelectMany(f => f).ToArray(), []);

    private static Sample Intent(Intent intent, params float[][] floats) =>
        new(Kit.Encode(intent), d => Kit.DecodeIntent(d), floats.SelectMany(f => f).ToArray(), []);

    private static Sample Msg(Message message, float[] floats, float[]? halves = null) =>
        new(Kit.Encode(message), d => MessageCodec.Read(d), floats, halves ?? []);

    private static Sample Record(string codec, object value, params float[][] floats)
    {
        var c = CodecCases.Get(codec);
        return new Sample(c.Encode(value), c.Decode, floats.SelectMany(f => f).ToArray(), []);
    }

    private static Sample Build(string key)
    {
        var entity = new EntityId(5);
        var position = V(1);
        var velocity = V(4);
        var state = new EntityState(
            new EntityId(42), EntityStateFields.Position | EntityStateFields.Velocity, 0, 3, position, Quaternion.Identity, new Vector3(1234, 1235, 1236), 0);
        var halves = new float[] { 1234, 1235, 1236 };
        var snapshot = new Snapshot(1, new Tick(1), 0, 0, [new SnapshotFrame(_frame, 0)], [state]);
        var terrainEdit = new TerrainEditOp(new PlanetId(7), TerrainShape.Sphere, position, S(4), TerrainEditMode.Add, 3);
        var header = new ConstructHeaderRecord(entity, _frame, 2, ConstructMode.Ship, position, _q1, 4);

        return key switch
        {
            "TerrainEditOp" => Op(terrainEdit, Components(position), [S(4)]),
            "ConstructCreateOp" => Op(
                new ConstructCreateOp(entity, _frame, position, _q1, new CellPos(1, 2, 3), BlockState.Create(1, 3)), Components(position), Components(_q1)),
            "EntityReparentOp" => Op(
                new EntityReparentOp(entity, _frame, 2, position, _q1, velocity, null), Components(position), Components(_q1), Components(velocity)),
            "PlayerJoinOp" => Op(
                new PlayerJoinOp(new PlayerId(7), new PlatformIdentity(IdentityPlatform.Steam, 1234), "a", entity, _frame, 0, position, _q1),
                Components(position),
                Components(_q1)),
            "PlayerLeaveOp" => Op(new PlayerLeaveOp(new PlayerId(7), _frame, 0, position, _q1, S(4)), Components(position), Components(_q1), [S(4)]),
            "TerrainToolIntent" => Intent(
                new TerrainToolIntent(new PlanetId(7), position, velocity, S(7), TerrainEditMode.Add, 1), Components(position), Components(velocity), [S(7)]),
            "PlaceBlockOnTerrain" => Intent(
                new PlaceBlockIntent(new TerrainTarget(new PlanetId(7), V(1), V(4), V(7)), 10, 3), Components(V(1)), Components(V(4)), Components(V(7))),
            "InputBatch" => Msg(new InputBatch(0, [new PlayerInput(0, 0f, 0f, S(1), S(2), InputButtons.None)]), [S(1), S(2)]),
            "Snapshot" => Msg(snapshot, Components(position), halves),
            "EntitySpawn" => Msg(
                new EntitySpawn(entity, EntityKind.Player, 1, null, new SnapshotFrame(_frame, 0), state, []), Components(position), halves),
            "ConstructState" => Msg(new ConstructState(header, 0, 1, []), [.. Components(position), .. Components(_q1)]),
            "ServerHello" => Msg(
                new ServerHello(
                    new WorldId(Guid.Empty),
                    new Tick(1),
                    20,
                    new PlayerId(1),
                    new EntityId(1),
                    [new PlanetParams(new PlanetId(1), new SystemId(1), 7, "p", S(1), S(2), S(3), S(4), S(5), S(6))],
                    [],
                    []),
                [S(1), S(2), S(3), S(4), S(5), S(6)]),
            "OperationApplied" => Msg(new OperationApplied(new OpId(1), new Tick(1), terrainEdit, []), [.. Components(position), S(4)]),
            "IntentSubmit" => Msg(
                new IntentSubmit(1, new TerrainToolIntent(new PlanetId(7), position, velocity, S(7), TerrainEditMode.Add, 1)),
                [.. Components(position), .. Components(velocity), S(7)]),
            "ConstructHeaderRecord" => Record("ConstructHeader", header, Components(position), Components(_q1)),
            "PlayerRecord" => Record(
                "Player",
                new PlayerRecord(new PlayerId(7), new PlatformIdentity(IdentityPlatform.Dev, 1), "a", entity, _frame, 0, null, position, _q2, S(4), true),
                Components(position),
                Components(_q2),
                [S(4)]),
            "PlanetParams" => Record(
                "Planet", new PlanetParams(new PlanetId(1), new SystemId(1), 7, "p", S(1), S(2), S(3), S(4), S(5), S(6)), [S(1), S(2), S(3), S(4), S(5), S(6)]),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    private static int IndexOf(byte[] data, byte[] pattern)
    {
        var found = -1;
        for (var i = 0; i + pattern.Length <= data.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                Assert.True(found < 0, $"pattern {Kit.ToHex(pattern)} occurs more than once in [{Kit.ToHex(data)}]; pick another sentinel");
                found = i;
            }
        }

        return found;
    }

    [Theory]
    [InlineData("TerrainEditOp")]
    [InlineData("ConstructCreateOp")]
    [InlineData("EntityReparentOp")]
    [InlineData("PlayerJoinOp")]
    [InlineData("PlayerLeaveOp")]
    [InlineData("TerrainToolIntent")]
    [InlineData("PlaceBlockOnTerrain")]
    [InlineData("InputBatch")]
    [InlineData("Snapshot")]
    [InlineData("EntitySpawn")]
    [InlineData("ConstructState")]
    [InlineData("ServerHello")]
    [InlineData("OperationApplied")]
    [InlineData("IntentSubmit")]
    [InlineData("ConstructHeaderRecord")]
    [InlineData("PlayerRecord")]
    [InlineData("PlanetParams")]
    public void Read_NaNOrInfinityInAnyFloatOrHalfField_ThrowsProtocolViolation(string key)
    {
        var sample = Build(key);
        _ = sample.Decode(sample.Encoded);

        foreach (var value in sample.Floats)
        {
            var at = IndexOf(sample.Encoded, BitConverter.GetBytes(value));
            Assert.True(at >= 0, $"{key}: float {value} not found in the encoding [{Kit.ToHex(sample.Encoded)}]");
            foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var copy = (byte[])sample.Encoded.Clone();
                BitConverter.GetBytes(bad).CopyTo(copy, at);
                Kit.AssertViolation(sample.Decode, copy);
            }
        }

        foreach (var value in sample.Halves)
        {
            var at = IndexOf(sample.Encoded, BitConverter.GetBytes((Half)value));
            Assert.True(at >= 0, $"{key}: half {value} not found in the encoding [{Kit.ToHex(sample.Encoded)}]");
            foreach (var bad in new byte[][] { [0x00, 0x7E], [0x00, 0x7C], [0x00, 0xFC], [0xFF, 0xFF] })
            {
                var copy = (byte[])sample.Encoded.Clone();
                bad.CopyTo(copy, at);
                Kit.AssertViolation(sample.Decode, copy);
            }
        }
    }
}
