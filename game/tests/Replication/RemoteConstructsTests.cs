using System.Diagnostics;
using System.Globalization;

using Godot;

using Horyzont.Core.World.Constructs;
using Horyzont.Game.Testing;

namespace Horyzont.Game.Tests.Replication;

public sealed class RemoteConstructsTests : EngineTestBase
{
    /// <summary>Engine budget of the 200 ms rule (X11): 200 ms minus the worst delivery of the 50 ms + 10 ms link and one tick of rounding.</summary>
    private const double EngineBudgetMilliseconds = 108;

    private Task Idle(RemoteHarness h) => WaitUntil(() => h.Constructs.World!.PendingJobs == 0, 600, "the remesh queue to drain");

    private static void AssertChunkMatchesMesher(RemoteHarness h, ChunkPos chunk)
    {
        var expected = h.Expected(chunk);
        var hasNode = h.Constructs.World!.TryGetConstruct(RemoteHarness.Construct, out var construct) && construct.TryGetChunk(chunk, out _);
        if (expected.IsEmpty)
        {
            EngineAssert.True(!hasNode, $"chunk {chunk} is empty but has a node");
            return;
        }

        EngineAssert.True(hasNode, $"chunk {chunk} has geometry but no node");
        h.Constructs.World.TryGetConstruct(RemoteHarness.Construct, out construct);
        construct!.TryGetChunk(chunk, out var node);
        var surfaces = !h.Render ? 0 : (expected.Cubes.IsEmpty ? 0 : 1) + (expected.Shapes.IsEmpty ? 0 : 1);
        EngineAssert.Equal(surfaces, node!.SurfaceCount, $"render surfaces of chunk {chunk}");
        EngineAssert.Equal(expected.CollisionFaces.Length / 3, node.CollisionTriangleCount, $"collision triangles of chunk {chunk}");
    }

    [EngineTest]
    public async Task RemoteConstructs_ResyncWhileMeshing_DiscardsOldMeshAndCollision()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var stall = 0;
        using var h = new RemoteHarness(Host, beforeBuild: _ =>
        {
            if (Volatile.Read(ref stall) == 1)
            {
                entered.Set();
                release.Wait();
            }
        });

        // First copy: two chunks, one of them holding a block type this client does not know (rendered as core:missing).
        var unknown = BlockState.Create(2047, 0);
        h.Publish(3, h.Chunk(new ChunkPos(0, 0, 0), (0, h.Stone), (1, unknown)), h.Chunk(new ChunkPos(1, 0, 0), (0, h.Stone)));
        await Idle(h);
        AssertChunkMatchesMesher(h, new ChunkPos(0, 0, 0));
        AssertChunkMatchesMesher(h, new ChunkPos(1, 0, 0));
        EngineAssert.True(h.Constructs.World!.TryGetConstruct(RemoteHarness.Construct, out var node) && node.TryGetChunk(new ChunkPos(1, 0, 0), out _),
            "the second chunk is shown before the resync");

        // An edit starts a build that stalls; the repair arrives meanwhile and no longer has the second chunk.
        Volatile.Write(ref stall, 1);
        h.Edit(70, 4, new CellPos(2, 0, 0));
        await WaitUntil(() => entered.IsSet, 600, "the edit build to stall in the mesher");
        h.Publish(9, h.Chunk(new ChunkPos(0, 0, 0), (0, h.Stone)));
        Volatile.Write(ref stall, 0);
        release.Set();
        await Idle(h);

        AssertChunkMatchesMesher(h, new ChunkPos(0, 0, 0));
        AssertChunkMatchesMesher(h, new ChunkPos(1, 0, 0));
        EngineAssert.True(
            !h.Constructs.World.TryGetConstruct(RemoteHarness.Construct, out node) || !node.TryGetChunk(new ChunkPos(1, 0, 0), out _),
            "the mesh and the collision of the old copy are gone");
        EngineAssert.True(h.Constructs.World.AreCollisionsReady(h.Planet, new System.Numerics.Vector3(5.5f, 4001.01f, 5.5f), 3), "the new copy has its collision");
    }

    [EngineTest(Slow = true)]
    public async Task Replicate_MirrorEditToMeshAndCollision_ReportsEngineLatency()
    {
        using var h = new RemoteHarness(Host);
        h.Publish(1, h.Chunk(new ChunkPos(0, 0, 0), (0, h.Stone)));
        await Idle(h);
        var feet = new System.Numerics.Vector3(5.5f, 4001.01f, 5.5f);
        EngineAssert.True(h.Constructs.World!.AreCollisionsReady(h.Planet, feet, 3), "the first copy has its collision");

        var delays = new List<double>();
        var version = 1u;
        var applied = false;
        h.Constructs.World.ChunkMeshApplied += (_, _) => applied = true;
        for (var i = 1; i <= 120; i++)
        {
            version++;
            applied = false;
            var received = Time.GetTicksUsec();
            h.Edit(100 + (ulong)i, version, new CellPos(i % 16, (i / 16) % 16, 0));
            await WaitUntil(() => applied && h.Constructs.World.AreCollisionsReady(h.Planet, feet, 3), 600, "the edited chunk to be meshed and collidable");
            delays.Add((Time.GetTicksUsec() - received) / 1000.0);
            await NextFrame();
        }

        delays.Sort();
        var p99 = delays[(int)(delays.Count * 0.99)];
        Report($"replicate_engine receive_to_mesh_and_collision_ms max={delays[^1].ToString("F1", CultureInfo.InvariantCulture)} p99={p99.ToString("F1", CultureInfo.InvariantCulture)} budget={EngineBudgetMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}");
        EngineAssert.AtMost(p99, EngineBudgetMilliseconds, "p99 from receipt to the new mesh and collision");
    }

    [EngineTest]
    public async Task RemoteConstructs_StateOperationsAndRemoval_FollowTheMirror()
    {
        using var h = new RemoteHarness(Host);
        h.Publish(1, h.Chunk(new ChunkPos(0, 0, 0), (0, h.Stone)));
        await Idle(h);
        AssertChunkMatchesMesher(h, new ChunkPos(0, 0, 0));
        h.Edit(10, 2, new CellPos(1, 0, 0));
        await Idle(h);
        AssertChunkMatchesMesher(h, new ChunkPos(0, 0, 0));
        h.Edit(11, 3, new CellPos(1, 0, 0), clear: true);
        await Idle(h);
        AssertChunkMatchesMesher(h, new ChunkPos(0, 0, 0));
        h.Mirror.Remove(RemoteHarness.Construct);
        await Idle(h);
        EngineAssert.True(!h.Constructs.World!.TryGetConstruct(RemoteHarness.Construct, out _), "the construct is unloaded");
        EngineAssert.Equal(0, h.Constructs.World.LoadedConstructCount, "no construct left");
    }

    [EngineTest]
    public async Task RemoteConstructs_Disconnect_ShutsTheWorldDownBeforeRelease()
    {
        var h = new RemoteHarness(Host);
        h.Publish(1, h.Chunk(new ChunkPos(0, 0, 0), (0, h.Stone)));
        await Idle(h);
        var watch = Stopwatch.StartNew();
        h.Constructs.OnDisconnected(Horyzont.Core.Protocol.DisconnectCode.Normal);
        await h.Constructs.Shutdown;
        EngineAssert.True(h.Constructs.World!.IsShutdown, "the world accepts no more work");
        EngineAssert.Equal(0, h.Constructs.World.AliveWorkerThreads, "no worker thread is left");
        EngineAssert.AtMost(watch.Elapsed.TotalSeconds, 5, "shutdown time");
        h.Dispose();
    }
}
