using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Frames;

public class FrameGraphTests
{
    private static readonly FrameRef _home = FrameRef.Of(WorldFactory.HomePlanet);
    private static readonly FrameRef _system1 = FrameRef.Of(new SystemId(1));

    [Fact]
    public void FrameGraph_NestedConstructs_ComposeInDouble()
    {
        // Seven constructs nested in the home planet: 8 parent links from the innermost one to the system frame.
        var headers = new List<ConstructHeaderRecord>();
        var parent = _home;
        for (var i = 1; i <= 7; i++)
        {
            var id = new EntityId((ulong)i);
            headers.Add(Header(id, parent, new Vector3(10.25f * i, 4000f, -3f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f * i)));
            parent = FrameRef.Of(id);
        }

        var graph = new FrameGraph(new WorldStateFramePoses(Build(headers), new HomeOnlyPlacement(WorldFactory.HomePlanet)));

        // Reference: the same chain composed by hand.
        var expected = PoseD.Identity;
        for (var i = headers.Count - 1; i >= 0; i--)
        {
            expected = FrameMath.Compose(FrameMath.FromFloat(headers[i].Origin, headers[i].Rotation), expected);
        }

        Assert.True(graph.TryGetWorldPose(FrameRef.Of(new EntityId(7)), out var world));
        Assert.InRange(Vector3D.Distance(expected.Position, world.Position), 0, 1e-9);
        Assert.InRange(Math.Abs(QuaternionD.Dot(expected.Rotation, world.Rotation)), 1 - 1e-12, 1 + 1e-12);
        Assert.True(graph.TryGetWorldPose(_home, out var planet));
        Assert.Equal(PoseD.Identity, planet);
        Assert.True(graph.TryGetWorldPose(_system1, out var system));
        Assert.Equal(PoseD.Identity, system);

        // Relative pose and expression between two nested frames.
        Assert.True(graph.TryRelative(FrameRef.Of(new EntityId(3)), FrameRef.Of(new EntityId(5)), out var relative));
        var direct = FrameMath.Compose(
            FrameMath.FromFloat(headers[3].Origin, headers[3].Rotation), FrameMath.FromFloat(headers[4].Origin, headers[4].Rotation));
        Assert.InRange(Vector3D.Distance(direct.Position, relative.Position), 0, 1e-9);
        Assert.True(graph.TryExpress(FrameRef.Of(new EntityId(5)), new Vector3(1, 2, 3), _home, out var inPlanet));
        Assert.True(graph.TryGetWorldPose(FrameRef.Of(new EntityId(5)), out var five));
        Assert.InRange(Vector3D.Distance(FrameMath.TransformPoint(five, new Vector3D(1, 2, 3)), inPlanet), 0, 1e-9);

        // Regions: the nearest planet or system ancestor.
        Assert.True(graph.TryGetRegion(FrameRef.Of(new EntityId(7)), out var region));
        Assert.Equal(_home, region);
        Assert.True(graph.TryGetRegion(_home, out region));
        Assert.Equal(_home, region);
        Assert.True(graph.TryGetRegion(_system1, out region));
        Assert.Equal(_system1, region);

        // One link more than FrameGraph.MaxDepth is refused.
        headers.Add(Header(new EntityId(8), FrameRef.Of(new EntityId(7)), Vector3.One, Quaternion.Identity));
        var deep = new FrameGraph(new WorldStateFramePoses(Build(headers), new HomeOnlyPlacement(WorldFactory.HomePlanet)));
        Assert.True(deep.TryGetWorldPose(FrameRef.Of(new EntityId(7)), out _));
        Assert.False(deep.TryGetWorldPose(FrameRef.Of(new EntityId(8)), out _));
        Assert.Equal(8, FrameGraph.MaxDepth);
    }

    [Fact]
    public void FrameGraph_ConstructInSystemFrame_UsesSystemAsRegion()
    {
        var ship = new EntityId(1);
        var state = Build([Header(ship, _system1, new Vector3(300_000f, 0f, 0f), Quaternion.Identity)]);
        var graph = new FrameGraph(new WorldStateFramePoses(state, new HomeOnlyPlacement(WorldFactory.HomePlanet)));

        Assert.True(graph.TryGetRegion(FrameRef.Of(ship), out var region));
        Assert.Equal(_system1, region);
        Assert.True(graph.TryExpress(FrameRef.Of(ship), new Vector3(1, 0, 0), _home, out var inPlanet));
        Assert.Equal(new Vector3D(300_001, 0, 0), inPlanet);
    }

    [Fact]
    public void FrameGraph_CycleOrMissingFrame_ReturnsFalse()
    {
        // The world loader refuses cycles and missing parents, so a hand-made source supplies them.
        var a = FrameRef.Of(new EntityId(1));
        var b = FrameRef.Of(new EntityId(2));
        var orphan = FrameRef.Of(new EntityId(3));
        var source = new TableSource();
        source.Add(_system1, default);
        source.Add(_home, _system1);
        source.Add(a, b);
        source.Add(b, a);
        source.Add(orphan, FrameRef.Of(new PlanetId(9)));
        var graph = new FrameGraph(source);

        Assert.True(graph.TryGetWorldPose(_home, out _));
        Assert.False(graph.TryGetWorldPose(a, out _));
        Assert.False(graph.TryGetRegion(a, out _));
        Assert.False(graph.TryGetWorldPose(orphan, out _));
        Assert.False(graph.TryGetRegion(orphan, out _));
        Assert.False(graph.TryGetWorldPose(FrameRef.Of(new EntityId(42)), out _));
        Assert.False(graph.TryGetWorldPose(default, out _));
        Assert.False(graph.TryRelative(_home, a, out _));
        Assert.False(graph.TryExpress(a, Vector3.Zero, _home, out _));

        // The world state source: unknown frames and other systems.
        var state = Build([Header(new EntityId(1), _home, Vector3.One, Quaternion.Identity)]);
        var world = new FrameGraph(new WorldStateFramePoses(state, new HomeOnlyPlacement(WorldFactory.HomePlanet)));
        Assert.True(world.TryGetWorldPose(FrameRef.Of(new EntityId(1)), out _));
        Assert.False(world.TryGetWorldPose(FrameRef.Of(new EntityId(42)), out _));
        Assert.False(world.TryGetWorldPose(FrameRef.Of(new SystemId(2)), out _));
        Assert.False(world.TryGetWorldPose(FrameRef.Of(new PlanetId(9)), out _));
        Assert.False(world.TryGetRegion(FrameRef.Of(new PlanetId(9)), out _));
    }

    [Fact]
    public void HomeOnlyPlacement_HomeIsIdentity_OthersUnknown()
    {
        var placement = new HomeOnlyPlacement(WorldFactory.HomePlanet);

        Assert.True(placement.TryGetPose(WorldFactory.HomePlanet, out var pose));
        Assert.Equal(PoseD.Identity, pose);
        Assert.False(placement.TryGetPose(new PlanetId(2), out _));
    }

    [Fact]
    public void FrameGraph_PlanetWithoutPlacement_ReturnsFalse()
    {
        var state = WorldFactory.CreateWith(WorldFactory.HomePlanetParams, new PlanetParams(new PlanetId(2), new SystemId(1), 3, "core:test/1"));
        var graph = new FrameGraph(new WorldStateFramePoses(state, new HomeOnlyPlacement(WorldFactory.HomePlanet)));

        Assert.True(graph.TryGetWorldPose(_home, out _));
        Assert.False(graph.TryGetWorldPose(FrameRef.Of(new PlanetId(2)), out _));

        // The region of a planet frame does not need its placement.
        Assert.True(graph.TryGetRegion(FrameRef.Of(new PlanetId(2)), out var region));
        Assert.Equal(FrameRef.Of(new PlanetId(2)), region);
    }

    [Fact]
    public void FrameGraph_ExpressInRegion_ComposesUpToRegionWithoutPlanetPlacement()
    {
        // Two nested constructs on a second planet that has no placement: the world pose is unknown, the region position is not.
        var second = FrameRef.Of(new PlanetId(2));
        var outer = Header(new EntityId(1), second, new Vector3(100f, 4000f, -20f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f));
        var inner = Header(new EntityId(2), FrameRef.Of(new EntityId(1)), new Vector3(3f, 1f, 2f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.25f));
        var builder = new WorldStateBuilder(WorldFactory.DefaultWorldId, 42);
        builder.SetProgress(default, default, 100, 1);
        builder.AddPlanet(WorldFactory.HomePlanetParams);
        builder.AddPlanet(new PlanetParams(new PlanetId(2), new SystemId(1), 3, "core:test/1"));
        builder.AddBlockType(new RegistryEntry(1, WorldFactory.StoneBlockKey));
        foreach (var header in new[] { outer, inner })
        {
            builder.AddConstruct(header);
            var blocks = new uint[ConstructChunk.CellCount];
            blocks[0] = BlockState.Create(1, 0).Packed;
            builder.AddChunk(new ConstructChunkRecord(new ConstructChunkKey(header.Id, default), blocks));
        }

        var graph = new FrameGraph(new WorldStateFramePoses(builder.Build(), new HomeOnlyPlacement(WorldFactory.HomePlanet)));
        var local = new Vector3(0.5f, -1f, 4f);
        Assert.False(graph.TryGetWorldPose(FrameRef.Of(new EntityId(2)), out _));

        Assert.True(graph.TryExpressInRegion(FrameRef.Of(new EntityId(2)), local, out var region, out var point));
        Assert.Equal(second, region);
        var expected = FrameMath.TransformPoint(
            FrameMath.Compose(FrameMath.FromFloat(outer.Origin, outer.Rotation), FrameMath.FromFloat(inner.Origin, inner.Rotation)),
            Vector3D.FromFloat(local));
        Assert.InRange(Vector3D.Distance(expected, point), 0, 1e-9);

        // A region frame expresses the point as it is; an unknown frame fails.
        Assert.True(graph.TryExpressInRegion(second, local, out region, out point));
        Assert.Equal(second, region);
        Assert.Equal(Vector3D.FromFloat(local), point);
        Assert.False(graph.TryExpressInRegion(FrameRef.Of(new EntityId(99)), local, out _, out _));
    }

    [Fact]
    public void FrameGraph_WorldPose_AllocatesZeroBytes()
    {
        var headers = new List<ConstructHeaderRecord>();
        var parent = _home;
        for (var i = 1; i <= 6; i++)
        {
            var id = new EntityId((ulong)i);
            headers.Add(Header(id, parent, new Vector3(i, 4000f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.2f)));
            parent = FrameRef.Of(id);
        }

        var graph = new FrameGraph(new WorldStateFramePoses(Build(headers), new HomeOnlyPlacement(WorldFactory.HomePlanet)));
        var deepest = FrameRef.Of(new EntityId(6));
        var middle = FrameRef.Of(new EntityId(3));
        var sink = 0.0;
        for (var i = 0; i < 100; i++)
        {
            sink += Query(graph, deepest, middle);
        }

        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10_000; i++)
            {
                sink += Query(graph, deepest, middle);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, smallest);
        Assert.True(double.IsFinite(sink));

        static double Query(FrameGraph graph, FrameRef deepest, FrameRef middle)
        {
            graph.TryGetWorldPose(deepest, out var world);
            graph.TryRelative(middle, deepest, out var relative);
            graph.TryExpress(deepest, Vector3.One, middle, out var point);
            graph.TryGetRegion(deepest, out _);
            graph.TryExpressInRegion(deepest, Vector3.One, out _, out var inRegion);
            return world.Position.X + relative.Position.Y + point.Z + inRegion.X;
        }
    }

    internal static ConstructHeaderRecord Header(EntityId id, FrameRef parent, Vector3 origin, Quaternion rotation, ConstructMode mode = ConstructMode.Static) =>
        new(id, parent, 0, mode, origin, rotation, 1);

    internal static WorldState Build(IEnumerable<ConstructHeaderRecord> headers)
    {
        var builder = new WorldStateBuilder(WorldFactory.DefaultWorldId, 42);
        builder.SetProgress(default, default, 100, 1);
        builder.AddPlanet(WorldFactory.HomePlanetParams);
        builder.AddBlockType(new RegistryEntry(1, WorldFactory.StoneBlockKey));
        foreach (var header in headers)
        {
            builder.AddConstruct(header);
            var blocks = new uint[ConstructChunk.CellCount];
            blocks[0] = BlockState.Create(1, 0).Packed;
            builder.AddChunk(new ConstructChunkRecord(new ConstructChunkKey(header.Id, default), blocks));
        }

        return builder.Build();
    }

    private sealed class TableSource : IFramePoseSource
    {
        private readonly Dictionary<FrameRef, FrameRef> _parents = [];

        public void Add(FrameRef frame, FrameRef parent) => _parents.Add(frame, parent);

        public bool TryGetParent(FrameRef frame, out FrameRef parent) => _parents.TryGetValue(frame, out parent);

        public bool TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD poseInParent)
        {
            poseInParent = new PoseD(new Vector3D(1, 0, 0), QuaternionD.Identity);
            return TryGetParent(frame, out parent);
        }
    }
}
