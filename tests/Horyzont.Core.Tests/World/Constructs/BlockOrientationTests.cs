using System.Numerics;

using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.World.Constructs;

public sealed class BlockOrientationTests
{
    // Golden table of docs/plans/K2-12-budowle.md, repeated independently of the implementation.
    private static readonly (Face Up, Face Forward)[] _golden =
    [
        (Face.PosY, Face.NegZ), (Face.PosY, Face.NegX), (Face.PosY, Face.PosZ), (Face.PosY, Face.PosX),
        (Face.NegY, Face.PosZ), (Face.NegY, Face.NegX), (Face.NegY, Face.NegZ), (Face.NegY, Face.PosX),
        (Face.PosX, Face.NegZ), (Face.PosX, Face.PosY), (Face.PosX, Face.PosZ), (Face.PosX, Face.NegY),
        (Face.NegX, Face.NegZ), (Face.NegX, Face.NegY), (Face.NegX, Face.PosZ), (Face.NegX, Face.PosY),
        (Face.PosZ, Face.PosY), (Face.PosZ, Face.NegX), (Face.PosZ, Face.NegY), (Face.PosZ, Face.PosX),
        (Face.NegZ, Face.NegY), (Face.NegZ, Face.NegX), (Face.NegZ, Face.PosY), (Face.NegZ, Face.PosX),
    ];

    private static Vector3 Normal(Face face) => face switch
    {
        Face.PosX => Vector3.UnitX,
        Face.NegX => -Vector3.UnitX,
        Face.PosY => Vector3.UnitY,
        Face.NegY => -Vector3.UnitY,
        Face.PosZ => Vector3.UnitZ,
        _ => -Vector3.UnitZ,
    };

    [Fact]
    public void Table_MatchesGoldenUpForward()
    {
        for (var i = 0; i < 24; i++)
        {
            Assert.Equal(_golden[i].Up, BlockOrientation.Up((byte)i));
            Assert.Equal(_golden[i].Forward, BlockOrientation.Forward((byte)i));
        }
    }

    [Fact]
    public void AllOrientations_AreDistinctProperRotations()
    {
        var seen = new HashSet<(Vector3, Vector3, Vector3)>();
        for (var i = 0; i < 24; i++)
        {
            var o = (byte)i;
            var right = Normal(BlockOrientation.Right(o));
            var up = Normal(BlockOrientation.Up(o));
            var back = -Normal(BlockOrientation.Forward(o));
            Assert.Equal(0f, Vector3.Dot(right, up));
            Assert.Equal(0f, Vector3.Dot(up, back));
            Assert.Equal(0f, Vector3.Dot(right, back));
            Assert.Equal(1f, Vector3.Dot(Vector3.Cross(right, up), back));
            Assert.True(seen.Add((right, up, back)));
        }

        Assert.Equal(24, seen.Count);
    }

    [Fact]
    public void Identity_IsZero_AndCountMatchesBlockState()
    {
        Assert.Equal(0, BlockOrientation.Identity);
        Assert.Equal(Face.PosY, BlockOrientation.Up(BlockOrientation.Identity));
        Assert.Equal(Face.NegZ, BlockOrientation.Forward(BlockOrientation.Identity));
        Assert.Equal(Face.PosX, BlockOrientation.Right(BlockOrientation.Identity));
        Assert.Equal(BlockState.OrientationCount, BlockOrientation.Count);
        Assert.True(BlockOrientation.IsValid(23));
        Assert.False(BlockOrientation.IsValid(24));
    }

    [Fact]
    public void FromUpForward_RoundTripsAll_AndRejectsParallel()
    {
        for (var i = 0; i < 24; i++)
        {
            var o = (byte)i;
            Assert.True(BlockOrientation.TryFromUpForward(BlockOrientation.Up(o), BlockOrientation.Forward(o), out var back));
            Assert.Equal(o, back);
            Assert.Equal(o, BlockOrientation.FromUpForward(BlockOrientation.Up(o), BlockOrientation.Forward(o)));
        }

        var parallel = 0;
        for (var a = 0; a < 6; a++)
        {
            for (var b = 0; b < 6; b++)
            {
                if (a >> 1 == b >> 1)
                {
                    parallel++;
                    Assert.False(BlockOrientation.TryFromUpForward((Face)a, (Face)b, out _));
                    Assert.Throws<ArgumentException>(() => BlockOrientation.FromUpForward((Face)a, (Face)b));
                }
            }
        }

        Assert.Equal(12, parallel);
    }

    [Fact]
    public void WithYaw_FourTurns_IsIdentity_KeepsUp()
    {
        for (var i = 0; i < 24; i++)
        {
            var o = (byte)i;
            Assert.Equal(o, BlockOrientation.WithYaw(o, 4));
            Assert.Equal(o, BlockOrientation.WithYaw(o, -8));
            Assert.Equal(BlockOrientation.Up(o), BlockOrientation.Up(BlockOrientation.WithYaw(o, 1)));
            Assert.Equal(BlockOrientation.WithYaw(o, 3), BlockOrientation.WithYaw(o, -1));

            var turned = o;
            var forwards = new HashSet<Face>();
            for (var k = 0; k < 4; k++)
            {
                forwards.Add(BlockOrientation.Forward(turned));
                turned = BlockOrientation.WithYaw(turned, 1);
            }

            Assert.Equal(4, forwards.Count);
        }
    }

    [Fact]
    public void WithYaw_QuarterTurn_IsCounterClockwiseAboutUp()
    {
        for (var i = 0; i < 24; i++)
        {
            var o = (byte)i;
            var up = Normal(BlockOrientation.Up(o));
            var forward = Normal(BlockOrientation.Forward(o));
            var turned = Normal(BlockOrientation.Forward(BlockOrientation.WithYaw(o, 1)));

            // A counter-clockwise quarter turn about Up (right-hand rule) maps f to up x f.
            Assert.Equal(Vector3.Cross(up, forward), turned);
        }
    }

    [Fact]
    public void WithUp_KeepsYaw_ReachesAll24()
    {
        var reached = new HashSet<byte>();
        for (var i = 0; i < 24; i++)
        {
            var o = (byte)i;
            for (var slot = 0; slot < 6; slot++)
            {
                var changed = BlockOrientation.WithUp(o, slot);
                Assert.Equal(slot, BlockOrientation.UpSlot(changed));
                Assert.Equal(BlockOrientation.Yaw(o), BlockOrientation.Yaw(changed));
                Assert.Equal(BlockOrientation.UpOfSlot(slot), BlockOrientation.Up(changed));
            }
        }

        // The picker of K2-13: UpSteps (next up slot) and YawSteps reach every index from the identity.
        var frontier = new Queue<byte>();
        frontier.Enqueue(0);
        reached.Add(0);
        while (frontier.Count > 0)
        {
            var o = frontier.Dequeue();
            foreach (var next in new[]
            {
                BlockOrientation.WithYaw(o, 1), BlockOrientation.WithYaw(o, -1),
                BlockOrientation.WithUp(o, (BlockOrientation.UpSlot(o) + 1) % 6),
            })
            {
                if (reached.Add(next))
                {
                    frontier.Enqueue(next);
                }
            }
        }

        Assert.Equal(24, reached.Count);
    }

    [Fact]
    public void ToConstruct_ToBlock_AreInverse_AndMatchQuaternion()
    {
        for (var i = 0; i < 24; i++)
        {
            var o = (byte)i;
            var q = BlockOrientation.ToQuaternion(o);
            for (var f = 0; f < 6; f++)
            {
                var blockFace = (Face)f;
                var constructFace = BlockOrientation.ToConstruct(o, blockFace);
                Assert.Equal(blockFace, BlockOrientation.ToBlock(o, constructFace));
                var rotated = Vector3.Transform(Normal(blockFace), q);
                Assert.True((rotated - Normal(constructFace)).Length() < 1e-6f, $"orientation {i} face {blockFace}");
            }
        }
    }

    [Fact]
    public void PublicMethods_RejectInvalidOrientation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockOrientation.Up(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockOrientation.Forward(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockOrientation.ToQuaternion(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockOrientation.WithUp(0, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockOrientation.UpOfSlot(-1));
    }
}
