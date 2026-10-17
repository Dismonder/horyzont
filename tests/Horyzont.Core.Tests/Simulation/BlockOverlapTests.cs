using System.Numerics;

using FsCheck.Xunit;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Simulation;

public sealed class BlockOverlapTests
{
    private static readonly Obb _unit = new(Vector3.Zero, Quaternion.Identity, new Vector3(0.5f));

    private static Obb Cube(float x, float y, float z, float yawDegrees = 0f, float pitchDegrees = 0f) =>
        new(new Vector3(x, y, z), Quaternion.CreateFromYawPitchRoll(yawDegrees * MathF.PI / 180f, pitchDegrees * MathF.PI / 180f, 0f), new Vector3(0.5f));

    [Theory]
    [InlineData(1.15f, true)] // no sample point of the turned cube lies in the first one, yet they intersect (D15)
    [InlineData(1.25f, false)] // the turned cube reaches 0.5 + 0.7071 = 1.2071 along X only
    public void Intersects_Cube45DegreesShifted_DecidedByExactGeometry(float shift, bool expected)
    {
        Assert.Equal(expected, BlockOverlap.Intersects(_unit, Cube(shift, 0f, 0f, 45f)));
    }

    [Theory]
    [InlineData(1f, 0f, 0f)] // face
    [InlineData(1f, 1f, 0f)] // edge
    [InlineData(1f, 1f, 1f)] // vertex
    [InlineData(0f, -1f, 0f)]
    public void Intersects_AxisAlignedTouching_False(float x, float y, float z)
    {
        Assert.False(BlockOverlap.Intersects(_unit, Cube(x, y, z)));
    }

    [Fact]
    public void Intersects_VertexOfTurnedCubeTouchesFace_False()
    {
        var shift = 0.5f + (MathF.Sqrt(2f) / 2f);

        Assert.False(BlockOverlap.Intersects(_unit, Cube(shift, 0f, 0f, 45f)));
        Assert.True(BlockOverlap.Intersects(_unit, Cube(shift - 0.01f, 0f, 0f, 45f)));
    }

    [Theory]
    [InlineData(0.99f, true)]
    [InlineData(1.01f, false)]
    public void Intersects_Turned90Degrees_ParallelAxesGiveNoNaN(float shift, bool expected)
    {
        var turned = BlockOverlap.Intersects(_unit, Cube(shift, 0f, 0f, 90f));
        var turnedBoth = BlockOverlap.Intersects(Cube(0f, 0f, 0f, 90f, 90f), Cube(shift, 0f, 0f, 180f, 270f));

        Assert.Equal(expected, turned);
        Assert.Equal(expected, turnedBoth);
    }

    [Property(MaxTest = 500)]
    public bool Intersects_IsSymmetric(byte ax, byte ay, byte az, byte ayaw, byte apitch, byte bx, byte by, byte bz, byte byaw, byte bpitch)
    {
        var a = Cube(ax / 100f, ay / 100f, az / 100f, ayaw * 360f / 256f, apitch * 360f / 256f);
        var b = Cube(bx / 100f, by / 100f, bz / 100f, byaw * 360f / 256f, bpitch * 360f / 256f);

        return BlockOverlap.Intersects(a, b) == BlockOverlap.Intersects(b, a);
    }

    [Property(MaxTest = 300)]
    public bool Intersects_FarApartBoxes_NeverIntersect(byte yaw, byte pitch)
    {
        var turned = Cube(1.8f, 0f, 0f, yaw * 360f / 256f, pitch * 360f / 256f);

        return !BlockOverlap.Intersects(_unit, turned);
    }

    [Fact]
    public void Intersects_ManyCalls_DoNotAllocate()
    {
        var turned = Cube(1.15f, 0f, 0f, 45f);
        for (var i = 0; i < 5000; i++)
        {
            _ = BlockOverlap.Intersects(_unit, turned);
        }

        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var hits = 0;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                hits += BlockOverlap.Intersects(_unit, turned) ? 1 : 0;
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(1000, hits);
        }

        var allocated = smallest;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void OverlapsConstruct_NestedConstruct_UsesComposedPose()
    {
        var state = WorldFactory.CreateDefault();
        var frame = FrameRef.Of(WorldFactory.HomePlanet);
        var stone = BlockState.Create(1, 0);
        var parent = state.Constructs.Create(new EntityId(1), frame, new Vector3(100f, 50f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));
        parent.SetBlock(new CellPos(0, 0, 0), stone);
        var child = state.Constructs.Create(new EntityId(2), FrameRef.Of(parent.Id), new Vector3(3f, 0f, 0f), Quaternion.Identity);
        child.SetBlock(new CellPos(0, 0, 0), stone);

        // The parent turns +X to -Z, so the child cell (3..4, 0..1, 0..1) is at planet (100..101, 50..51, -4..-3).
        var inside = new Vector3(100.5f, 50.5f, -3.5f);
        var beside = new Vector3(102.5f, 50.5f, -3.5f);

        Assert.True(BlockOverlap.OverlapsConstruct(state.Constructs, frame, inside, Quaternion.Identity, skip: new EntityId(1)));
        Assert.False(BlockOverlap.OverlapsConstruct(state.Constructs, frame, beside, Quaternion.Identity, skip: null));
        Assert.False(BlockOverlap.OverlapsConstruct(state.Constructs, frame, inside, Quaternion.Identity, skip: new EntityId(2)));
    }

    [Fact]
    public void BlockOverlap_NonNestedPlacement_GoldenCasesUnchanged()
    {
        // K5-20: depth 1 keeps the float pose bit for bit; deeper nesting composes in double through FrameGraph and narrows once.
        var state = WorldFactory.CreateDefault();
        var frame = FrameRef.Of(WorldFactory.HomePlanet);
        var rotation = Quaternion.CreateFromYawPitchRoll(0.31f, -0.2f, 0.07f);
        var outer = state.Constructs.Create(new EntityId(1), frame, new Vector3(2400.567f, 3011.3f, -1387.65f), rotation);
        var middle = state.Constructs.Create(new EntityId(2), FrameRef.Of(outer.Id), new Vector3(3.25f, 1f, -7f), rotation);
        var inner = state.Constructs.Create(new EntityId(3), FrameRef.Of(middle.Id), new Vector3(-2f, 0.5f, 4f), rotation);

        Assert.True(BlockOverlap.TryGetConstructPose(state.Constructs, outer, frame, out var origin, out var turned));
        Assert.Equal(outer.Origin, origin);
        Assert.Equal(outer.Rotation, turned);

        var graph = new FrameGraph(new WorldStateFramePoses(state, new HomeOnlyPlacement(WorldFactory.HomePlanet)));
        Assert.True(graph.TryRelative(frame, FrameRef.Of(inner.Id), out var expected));
        Assert.True(FrameMath.TryToFloat(expected, FrameKind.Planet, out var expectedOrigin, out var expectedRotation));
        Assert.True(BlockOverlap.TryGetConstructPose(state.Constructs, inner, frame, out origin, out turned));
        Assert.Equal(expectedOrigin, origin);
        Assert.Equal(expectedRotation, turned);

        // A construct whose chain does not reach the frame has no pose there.
        Assert.False(BlockOverlap.TryGetConstructPose(state.Constructs, inner, FrameRef.Of(new PlanetId(2)), out _, out _));
    }

    [Fact]
    public void OverlapsConstruct_FaceContactAtPlanetRadius_ToleratesFloatGrain()
    {
        // Planet-frame coordinates near 4000 m have a half-ulp of ~1.2e-4 m; the origin of B, the rotation and the cell centre of the
        // new block (computed in B's grid, so exactly in contact) add up past the old 1e-4 m tolerance. Face contact must stay a touch.
        var frame = FrameRef.Of(WorldFactory.HomePlanet);
        var stone = BlockState.Create(1, 0);
        var worstDepth = 0f;
        for (var step = 0; step < 400; step++)
        {
            var state = WorldFactory.CreateDefault();
            var originB = new Vector3(2400.567f + (step * 0.0137f), 3011.3f + (step * 0.0211f), -1387.65f - (step * 0.0173f));
            var rotation = Quaternion.CreateFromYawPitchRoll(0.3f + (step * 0.0157f), 0.2f + (step * 0.0093f), 0.1f + (step * 0.0071f));
            var b = state.Constructs.Create(new EntityId(1), frame, originB, rotation);
            b.SetBlock(new CellPos(0, 0, 0), stone);
            var inverse = Quaternion.Inverse(rotation);

            // The new block sits in each of the six cells next to B's block, on the grid of B.
            var cells = new[] { new Vector3(1.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 1.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 1.5f), new Vector3(0.5f, 0.5f, -0.5f) };
            foreach (var local in cells)
            {
                var center = originB + Vector3.Transform(local, rotation);
                var seen = Vector3.Transform(center - originB, inverse);
                var error = Vector3.Abs(seen - local);
                var depth = MathF.Max(error.X, MathF.Max(error.Y, error.Z));
                worstDepth = MathF.Max(worstDepth, depth);

                Assert.False(
                    BlockOverlap.OverlapsConstruct(state.Constructs, frame, center, rotation, skip: null),
                    $"step {step}, cell {local}");
            }
        }

        // The premise: some of these placements are off the exact contact by more than the previous tolerance.
        Assert.True(worstDepth > 1e-4f, $"worst {worstDepth}");
        Assert.True(worstDepth < BlockOverlap.TouchToleranceMeters, $"worst {worstDepth}");
    }
}
