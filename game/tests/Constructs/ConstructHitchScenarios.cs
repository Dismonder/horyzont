using System.Diagnostics;

using Godot;

using Horyzont.Constructs.Sources;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Constructs;
using Horyzont.Game.Testing;
using Horyzont.Testing;

namespace Horyzont.Game.Tests.Constructs;

/// <summary>Frame-time scenarios of a 64³ construct, shared by the headless (CI) tests and the manual measurement with a GPU.</summary>
internal static class ConstructHitchScenarios
{
    public const double MaxFrameMilliseconds = 50;
    public const double MaxRemeshP99Milliseconds = 100;

    public static async Task<FrameStats> LoadInAsync(EngineTestBase test, ConstructPattern pattern, bool assertLimit, bool withCamera = false)
    {
        var state = ConstructFixtures.Create(pattern);
        using var w = new ConstructTestWorld(test.Host, state);
        var view = withCamera ? AddCamera(test.Host) : null;
        var probe = new FrameProbe();
        test.Host.AddChild(probe);
        await test.Tree.ToSignal(test.Tree, SceneTree.SignalName.ProcessFrame);

        probe.Reset();
        ApplyTimings.Reset();
        var gc0 = GC.CollectionCount(0);
        var gc2 = GC.CollectionCount(2);
        var pause0 = GC.GetTotalPauseDuration();
        w.World.RebuildAll(ConstructTestWorld.Planet);
        var frames = 0;
        while (w.World.PendingJobs > 0 && frames++ < 20000)
        {
            await test.Tree.ToSignal(test.Tree, SceneTree.SignalName.ProcessFrame);
        }

        EngineAssert.Equal(0, w.World.PendingJobs, $"{pattern}: pending jobs after the load-in");
        for (var i = 0; i < 60; i++)
        {
            await test.Tree.ToSignal(test.Tree, SceneTree.SignalName.ProcessFrame);
        }

        var stats = FrameStats.From(probe.Intervals);
        GD.Print($"    LoadIn {pattern}: frames={stats.Count} p50={stats.P50:F1} ms p99={stats.P99:F1} ms max={stats.Max:F1} ms " +
                 $"(load took {frames} frames) gc0={GC.CollectionCount(0) - gc0} gc2={GC.CollectionCount(2) - gc2} gcPause={(GC.GetTotalPauseDuration() - pause0).TotalMilliseconds:F1} ms");
        GD.Print($"      apply: upload n={ApplyTimings.Uploads} sum={ApplyTimings.UploadMilliseconds:F0} max={ApplyTimings.MaxUploadMilliseconds:F1} ms; collision n={ApplyTimings.Collisions} sum={ApplyTimings.CollisionMilliseconds:F0} max={ApplyTimings.MaxCollisionMilliseconds:F1} ms");
        probe.QueueFree();
        view?.QueueFree();
        if (assertLimit)
        {
            EngineAssert.AtMost(stats.Max, MaxFrameMilliseconds, $"{pattern}: longest frame during the load-in (ms)");
        }

        return stats;
    }

    public static async Task<(FrameStats Frames, FrameStats Remesh)> EditStreamAsync(EngineTestBase test, bool assertLimits, bool withCamera = false)
    {
        var state = ConstructFixtures.Create(ConstructPattern.Building);
        using var w = new ConstructTestWorld(test.Host, state);
        var view = withCamera ? AddCamera(test.Host) : null;
        w.World.RebuildAll(ConstructTestWorld.Planet);
        var frames = 0;
        while (w.World.PendingJobs > 0 && frames++ < 20000)
        {
            await test.Tree.ToSignal(test.Tree, SceneTree.SignalName.ProcessFrame);
        }

        var probe = new FrameProbe();
        test.Host.AddChild(probe);
        await test.Tree.ToSignal(test.Tree, SceneTree.SignalName.ProcessFrame);

        var pending = new Dictionary<ConstructChunkKey, long>();
        var latencies = new List<double>();
        w.World.ChunkMeshApplied += (id, chunk) =>
        {
            if (pending.Remove(new ConstructChunkKey(id, chunk), out var started))
            {
                latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        };

        var construct = ConstructFixtures.ConstructId;
        var random = new SplitMix(0xED17);
        var dirty = new List<ConstructChunkKey>();
        var created = new List<Horyzont.Core.Ids.EntityId>();
        var removed = new List<Horyzont.Core.Ids.EntityId>();
        var edits = 0;
        var maxApply = 0.0;
        probe.Reset();
        ApplyTimings.Reset();
        var gcEdit0 = GC.CollectionCount(2);
        var start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start).TotalSeconds < 10)
        {
            var due = (int)(Stopwatch.GetElapsedTime(start).TotalSeconds * 20) - edits;
            for (var i = 0; i < due; i++)
            {
                var wantsClear = random.Next(2) == 0;
                var cell = PickCell(random, state.Constructs.All[0], wantsClear);
                var stone = w.TypeId("core:stone_block");
                Horyzont.Core.Operations.Operation operation = wantsClear
                    ? new Horyzont.Core.Operations.BlockClearOp(construct, cell)
                    : new Horyzont.Core.Operations.BlockSetOp(construct, cell, BlockState.Create(stone, 0));
                var now = Stopwatch.GetTimestamp();
                var applied = w.Apply(operation);
                maxApply = Math.Max(maxApply, Stopwatch.GetElapsedTime(now).TotalMilliseconds);
                dirty.Clear();
                ConstructDirtyChunks.Collect(applied.Operation, applied.ConstructChanges, dirty, created, removed);
                foreach (var key in dirty)
                {
                    pending.TryAdd(key, now);
                }

                edits++;
            }

            await test.Tree.ToSignal(test.Tree, SceneTree.SignalName.ProcessFrame);
        }

        var drain = 0;
        while (w.World.PendingJobs > 0 && drain++ < 5000)
        {
            await test.Tree.ToSignal(test.Tree, SceneTree.SignalName.ProcessFrame);
        }

        var frameStats = FrameStats.From(probe.Intervals);
        var remeshStats = FrameStats.From(latencies);
        GD.Print($"    EditStream: edits={edits} frames p50={frameStats.P50:F1} p99={frameStats.P99:F1} max={frameStats.Max:F1} ms; " +
                 $"apply op max={maxApply:F1} ms; remesh latency n={remeshStats.Count} p50={remeshStats.P50:F1} p99={remeshStats.P99:F1} max={remeshStats.Max:F1} ms");
        GD.Print($"      apply: upload n={ApplyTimings.Uploads} sum={ApplyTimings.UploadMilliseconds:F0} max={ApplyTimings.MaxUploadMilliseconds:F1} ms; collision n={ApplyTimings.Collisions} sum={ApplyTimings.CollisionMilliseconds:F0} max={ApplyTimings.MaxCollisionMilliseconds:F1} ms; gc2={GC.CollectionCount(2) - gcEdit0}");
        probe.QueueFree();
        view?.QueueFree();
        if (assertLimits)
        {
            EngineAssert.AtMost(frameStats.Max, MaxFrameMilliseconds, "longest frame during the edit stream (ms)");
            EngineAssert.AtMost(remeshStats.P99, MaxRemeshP99Milliseconds, "p99 of OnApplied to ChunkMeshApplied (ms)");
        }

        return (frameStats, remeshStats);
    }

    /// <summary>A camera, a light and a sky over the construct so that the GPU draws it (manual measurement).</summary>
    private static Node3D AddCamera(Node3D host)
    {
        var origin = new Vector3(0, 4000, 0);
        var view = new Node3D { Name = "View" };
        host.AddChild(view);
        view.AddChild(new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-50, -35, 0), ShadowEnabled = true });
        view.AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() } },
        });
        var camera = new Camera3D { Name = "Camera", Far = 2000, Current = true };
        view.AddChild(camera);
        camera.Position = origin + new Vector3(70, 40, 120);
        camera.LookAt(origin + new Vector3(32, 20, 32));
        return view;
    }

    private static CellPos PickCell(SplitMix random, Construct construct, bool occupied)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var cell = new CellPos(random.Next(ConstructFixtures.Size), random.Next(ConstructFixtures.Size), random.Next(ConstructFixtures.Size));
            if (construct.GetBlock(cell).IsEmpty != occupied)
            {
                return cell;
            }
        }

        return occupied ? construct.BoundsMin : new CellPos(32, 32, 32);
    }

    /// <summary>SplitMix64: stable across runtimes.</summary>
    private sealed class SplitMix(ulong seed)
    {
        private ulong _state = seed;

        public int Next(int bound)
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (int)((z ^ (z >> 31)) % (ulong)bound);
        }
    }
}

/// <summary>Percentiles of a list of durations in milliseconds.</summary>
internal readonly record struct FrameStats(int Count, double P50, double P99, double Max)
{
    public static FrameStats From(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return new FrameStats(0, 0, 0, 0);
        }

        var sorted = values.Order().ToArray();
        return new FrameStats(sorted.Length, sorted[sorted.Length / 2], sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))], sorted[^1]);
    }
}
