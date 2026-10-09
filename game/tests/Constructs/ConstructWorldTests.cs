using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Constructs;
using Horyzont.Game.Physics;
using Horyzont.Game.Testing;
using Horyzont.Testing;

namespace Horyzont.Game.Tests.Constructs;

public sealed class ConstructWorldTests : EngineTestBase
{
    private Task Idle(ConstructTestWorld w) => WaitUntil(() => w.World.PendingJobs == 0, 600, "the remesh queue to drain");

    private static void AssertChunkMatchesMesher(ConstructTestWorld w, EntityId id, ChunkPos chunk)
    {
        var expected = w.Expected(id, chunk);
        var hasNode = w.World.TryGetConstruct(id, out var construct) && construct.TryGetChunk(chunk, out _);
        if (expected.IsEmpty)
        {
            EngineAssert.True(!hasNode, $"chunk {chunk} is empty but has a node");
            return;
        }

        EngineAssert.True(hasNode, $"chunk {chunk} has geometry but no node");
        w.World.TryGetConstruct(id, out construct);
        construct!.TryGetChunk(chunk, out var node);
        var surfaces = (expected.Cubes.IsEmpty ? 0 : 1) + (expected.Shapes.IsEmpty ? 0 : 1);
        EngineAssert.Equal(surfaces, node!.SurfaceCount, $"render surfaces of chunk {chunk}");
        EngineAssert.Equal(expected.CollisionFaces.Length / 3, node.CollisionTriangleCount, $"collision triangles of chunk {chunk}");
    }

    [EngineTest]
    public async Task CreateSetClear_MeshAndCollisionMatchMesher()
    {
        using var w = new ConstructTestWorld(Host, ConstructTestWorld.NewState(Horyzont.Game.Constructs.BlockContentLoader.Load()));
        var applied = new List<(EntityId, ChunkPos)>();
        w.World.ChunkMeshApplied += (id, chunk) => applied.Add((id, chunk));

        var id = w.Create(new CellPos(14, 3, 3));
        await Idle(w);
        AssertChunkMatchesMesher(w, id, new ChunkPos(0, 0, 0));
        EngineAssert.True(applied.Contains((id, new ChunkPos(0, 0, 0))), "ChunkMeshApplied for the created chunk");

        // A block on the chunk face dirties the neighbour chunk too (the border of its mesh changes).
        applied.Clear();
        w.Set(id, new CellPos(15, 3, 3), "core:wood_planks");
        w.Set(id, new CellPos(16, 3, 3), "core:stone_slab", orientation: 5);
        w.Set(id, new CellPos(16, 4, 3), "core:stone_block");
        await Idle(w);
        foreach (var chunk in new[] { new ChunkPos(0, 0, 0), new ChunkPos(1, 0, 0) })
        {
            AssertChunkMatchesMesher(w, id, chunk);
            EngineAssert.True(applied.Contains((id, chunk)), $"ChunkMeshApplied for dirty chunk {chunk}");
        }

        EngineAssert.True(w.World.TryGetConstruct(id, out var node) && node.ChunkCount == 2, "two chunk nodes");
        node!.TryGetChunk(new ChunkPos(1, 0, 0), out var shapeChunk);
        EngineAssert.Equal(2, shapeChunk!.SurfaceCount, "cube surface + prefab shape surface");

        applied.Clear();
        w.Clear(id, new CellPos(16, 3, 3));
        w.Clear(id, new CellPos(16, 4, 3));
        await Idle(w);
        AssertChunkMatchesMesher(w, id, new ChunkPos(0, 0, 0));
        AssertChunkMatchesMesher(w, id, new ChunkPos(1, 0, 0));
        EngineAssert.True(node.ChunkCount == 1, "the emptied chunk node was removed");

        // Removing the last blocks removes the construct and its node.
        w.Clear(id, new CellPos(15, 3, 3));
        w.Clear(id, new CellPos(14, 3, 3));
        await Idle(w);
        EngineAssert.True(!w.World.TryGetConstructNode(id, out _), "the construct node is gone");
        EngineAssert.Equal(0, w.World.LoadedConstructCount, "loaded constructs");
    }

    [EngineTest]
    public async Task CollisionOnly_NoMeshInstances()
    {
        var catalog = Horyzont.Game.Constructs.BlockContentLoader.Load();
        var options = new ConstructWorldOptions { Mode = ConstructWorldMode.CollisionOnly };
        using var w = new ConstructTestWorld(Host, ConstructTestWorld.NewState(catalog), options);
        var id = w.Create(new CellPos(0, 0, 0));
        w.Set(id, new CellPos(1, 0, 0));
        w.Set(id, new CellPos(20, 0, 0));
        await Idle(w);

        EngineAssert.True(w.Visuals is null, "no visuals in collision-only mode");
        w.World.TryGetConstruct(id, out var node);
        EngineAssert.Equal(2, node!.ChunkCount, "chunk nodes");
        foreach (var chunk in node.Chunks)
        {
            EngineAssert.True(chunk.MeshInstance is null, $"chunk {chunk.Chunk} must have no MeshInstance3D");
            EngineAssert.True(chunk.Body is not null, $"chunk {chunk.Chunk} must have a collision body");
            EngineAssert.Equal(CollisionLayers.ConstructsMask, chunk.Body!.CollisionLayer, "collision layer");
        }

        EngineAssert.Equal(0, CountMeshInstances(w.World), "MeshInstance3D nodes under the world");
    }

    [EngineTest]
    public async Task PhysicsRay_HitsSameCellAsConstructRaycast()
    {
        var state = ConstructFixtures.Create(ConstructPattern.Building, cubesOnly: true);
        using var w = new ConstructTestWorld(Host, state);
        w.World.RebuildAll(ConstructTestWorld.Planet);
        await Idle(w);
        await PhysicsFrames(2);

        var construct = state.Constructs.All[0];
        var centre = construct.LocalToParent(new System.Numerics.Vector3(32, 32, 32));
        var space = Host.GetWorld3D().DirectSpaceState;
        var random = new Random(20260709);
        var hits = 0;
        for (var i = 0; i < 200; i++)
        {
            var direction = RandomUnit(random);
            var origin = centre + (direction * 110f);
            var target = centre + new System.Numerics.Vector3(
                (float)((random.NextDouble() * 60) - 30), (float)((random.NextDouble() * 60) - 30), (float)((random.NextDouble() * 60) - 30));
            var ray = System.Numerics.Vector3.Normalize(target - origin);
            var coreHit = ConstructRaycast.Raycast(state.Constructs, ConstructTestWorld.Planet, origin, ray, 300f, out var core);

            var query = PhysicsRayQueryParameters3D.Create(
                ConstructNode.ToGodot(origin), ConstructNode.ToGodot(origin + (ray * 300f)), CollisionLayers.ConstructsMask);
            var result = space.IntersectRay(query);
            var physicsHit = result.Count > 0;
            EngineAssert.Equal(coreHit, physicsHit, $"ray {i}: hit/miss");
            if (!coreHit)
            {
                continue;
            }

            hits++;
            var position = (Vector3)result["position"];
            var normal = (Vector3)result["normal"];
            var local = construct.ParentToLocal(new System.Numerics.Vector3((float)position.X, (float)position.Y, (float)position.Z));
            var face = FaceOf(normal);
            var inside = new System.Numerics.Vector3(
                local.X - (FaceNormal(face).X * 0.01f), local.Y - (FaceNormal(face).Y * 0.01f), local.Z - (FaceNormal(face).Z * 0.01f));
            var cell = new CellPos((int)MathF.Floor(inside.X), (int)MathF.Floor(inside.Y), (int)MathF.Floor(inside.Z));
            EngineAssert.Equal(core.Cell, cell, $"ray {i}: cell");
            EngineAssert.Equal(core.Face, face, $"ray {i}: face");
        }

        EngineAssert.True(hits >= 100, $"only {hits} of 200 rays hit the building");
        Report($"200 rays, {hits} hits, physics engine {ProjectSettings.GetSetting("physics/3d/physics_engine")}");
    }

    [EngineTest]
    public async Task RemovedDuringMeshing_DropsResult()
    {
        using var w = new ConstructTestWorld(Host, ConstructTestWorld.NewState(Horyzont.Game.Constructs.BlockContentLoader.Load()));
        var applied = 0;
        w.World.ChunkMeshApplied += (_, _) => applied++;
        var id = w.Create(new CellPos(2, 2, 2));

        w.World.Pump(0); // starts the job on a worker; the result is not applied yet
        EngineAssert.Equal(1, w.World.PendingJobs, "one running job");
        w.Clear(id, new CellPos(2, 2, 2)); // removes the construct while its chunk is being meshed
        applied = 0;
        await Idle(w);
        for (var i = 0; i < 10; i++)
        {
            await NextFrame();
        }

        EngineAssert.True(!w.World.TryGetConstructNode(id, out _), "the removed construct has no node");
        EngineAssert.Equal(0, applied, "chunk results applied for the removed construct");
        EngineAssert.Equal(0, CountMeshInstances(w.World), "MeshInstance3D nodes under the world");
    }

    [EngineTest]
    public async Task Shutdown_JoinsWorkers_NoLateCallbacks()
    {
        using var w = new ConstructTestWorld(Host, ConstructTestWorld.NewState(Horyzont.Game.Constructs.BlockContentLoader.Load()));
        var applied = 0;
        w.World.ChunkMeshApplied += (_, _) => applied++;
        var id = w.Create(new CellPos(0, 0, 0));
        for (var x = 1; x < 40; x++)
        {
            w.Set(id, new CellPos(x, 0, 0));
        }

        w.World.Pump(0); // jobs are in flight
        var first = w.World.ShutdownAsync();
        var second = w.World.ShutdownAsync();
        EngineAssert.True(ReferenceEquals(first, second), "ShutdownAsync is idempotent");
        await first;

        EngineAssert.True(w.World.IsShutdown, "IsShutdown");
        EngineAssert.Equal(0, w.World.AliveWorkerThreads, "worker threads alive after shutdown");
        var before = applied;
        for (var i = 0; i < 20; i++)
        {
            await NextFrame();
        }

        EngineAssert.Equal(before, applied, "callbacks after shutdown");
        w.Set(id, new CellPos(0, 1, 0)); // ignored after shutdown
        w.World.Pump(4);
        EngineAssert.Equal(before, applied, "callbacks after an operation following shutdown");
    }

    private static int CountMeshInstances(Node root)
    {
        var count = root is MeshInstance3D ? 1 : 0;
        foreach (var child in root.GetChildren())
        {
            count += CountMeshInstances(child);
        }

        return count;
    }

    private static System.Numerics.Vector3 RandomUnit(Random random)
    {
        while (true)
        {
            var v = new System.Numerics.Vector3(
                (float)((random.NextDouble() * 2) - 1), (float)((random.NextDouble() * 2) - 1), (float)((random.NextDouble() * 2) - 1));
            var length = v.Length();
            if (length is > 0.1f and <= 1f)
            {
                return v / length;
            }
        }
    }

    private static Face FaceOf(Vector3 normal)
    {
        var ax = Math.Abs(normal.X);
        var ay = Math.Abs(normal.Y);
        var az = Math.Abs(normal.Z);
        if (ax >= ay && ax >= az)
        {
            return normal.X > 0 ? Face.PosX : Face.NegX;
        }

        if (ay >= az)
        {
            return normal.Y > 0 ? Face.PosY : Face.NegY;
        }

        return normal.Z > 0 ? Face.PosZ : Face.NegZ;
    }

    private static System.Numerics.Vector3 FaceNormal(Face face) => face switch
    {
        Face.PosX => System.Numerics.Vector3.UnitX,
        Face.NegX => -System.Numerics.Vector3.UnitX,
        Face.PosY => System.Numerics.Vector3.UnitY,
        Face.NegY => -System.Numerics.Vector3.UnitY,
        Face.PosZ => System.Numerics.Vector3.UnitZ,
        _ => -System.Numerics.Vector3.UnitZ,
    };
}
