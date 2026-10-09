using GdUnit4;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Terrain;
using Horyzont.Game.Tests.Support;
using Horyzont.Persistence.Terrain;
using Horyzont.Testing;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

[TestSuite]
public class TerrainEditTests
{
    private const float S = TerrainTestWorld.Surface;

    /// <summary>Centres on block faces, edges and corners (negative coordinates too) and inside blocks, near the ground.</summary>
    private static List<(Vector3 Center, float Radius, TerrainEditMode Mode)> RandomEdits(int count, int seed, int spread)
    {
        var random = new Random(seed);
        var edits = new List<(Vector3, float, TerrainEditMode)>(count);
        float Coordinate(float around)
        {
            var value = around + random.Next(-spread, spread + 1);
            return random.Next(3) switch
            {
                0 => MathF.Floor(value / 16f) * 16f,
                1 => (MathF.Floor(value / 16f) * 16f) + ((random.Next(2) * 2) - 1) * 0.001f,
                _ => value + (float)random.NextDouble(),
            };
        }

        for (var i = 0; i < count; i++)
        {
            var center = new Vector3(Coordinate(0), Coordinate(S), Coordinate(0));
            var radius = 0.5f + ((float)random.NextDouble() * 2.5f);
            edits.Add((center, radius, random.Next(2) == 0 ? TerrainEditMode.Add : TerrainEditMode.Remove));
        }

        return edits;
    }

    [TestCase(Timeout = 900000)]
    [RequireGodotRuntime]
    public async Task Edit_Footprint_WithinVersionedBlocks()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        world.AddViewer(TerrainTestWorld.Ground(0, 0), 96);
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 48)).IsTrue();
        var keys = new List<TerrainBlockKey>();
        using var before = new VoxelBuffer();
        using var after = new VoxelBuffer();
        var tool = world.Node.Terrain.GetVoxelTool();
        var changed = 0;
        var outside = 0;

        foreach (var (center, radius, mode) in RandomEdits(1000, 1, 24))
        {
            TerrainMath.AffectedBlocks(WorldFactory.HomePlanet, center, radius, 1f, TerrainRules.EditMarginVoxels, keys);
            var versioned = keys.Select(k => (k.X, k.Y, k.Z)).ToHashSet();
            var min = new Vector3I(keys.Min(k => k.X) - 1, keys.Min(k => k.Y) - 1, keys.Min(k => k.Z) - 1);
            var max = new Vector3I(keys.Max(k => k.X) + 1, keys.Max(k => k.Y) + 1, keys.Max(k => k.Z) + 1);
            var size = (max - min + Vector3I.One) * 16;
            var origin = min * 16;
            AssertThat(tool.IsAreaEditable(new Aabb(origin, size))).IsTrue();
            before.Create(size.X, size.Y, size.Z);
            tool.Copy(origin, before, TerrainChannels.UsedChannelsMask);
            world.Edit(center, radius, mode);
            after.Create(size.X, size.Y, size.Z);
            tool.Copy(origin, after, TerrainChannels.UsedChannelsMask);

            var sdf0 = before.GetChannelAsByteArray(TerrainChannels.Sdf);
            var sdf1 = after.GetChannelAsByteArray(TerrainChannels.Sdf);
            var mat0 = before.GetChannelAsByteArray(TerrainChannels.Material);
            var mat1 = after.GetChannelAsByteArray(TerrainChannels.Material);
            for (var z = 0; z < size.Z; z++)
            {
                for (var x = 0; x < size.X; x++)
                {
                    for (var y = 0; y < size.Y; y++)
                    {
                        // VoxelBuffer layout is ZXY: y varies fastest.
                        var i = y + (size.Y * (x + (size.X * z)));
                        if (sdf0[2 * i] == sdf1[2 * i] && sdf0[(2 * i) + 1] == sdf1[(2 * i) + 1] && mat0[i] == mat1[i])
                        {
                            continue;
                        }

                        changed++;
                        var p = origin + new Vector3I(x, y, z);
                        if (!versioned.Contains((p.X >> 4, p.Y >> 4, p.Z >> 4)))
                        {
                            outside++;
                        }
                    }
                }
            }
        }

        AssertThat(changed).IsGreater(10000);
        AssertThat(outside).IsEqual(0);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Edit_CopyRightAfterApply_EqualsLaterRead()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        world.AddViewer(TerrainTestWorld.Ground(0, 0));
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 24)).IsTrue();

        var applied = world.Edit(new Vector3(-0.5f, S, 15.9f), 3f, TerrainEditMode.Remove);
        var op = (TerrainEditOp)applied.Operation;
        var keys = world.State.TerrainVersions.All.Select(v => v.Key).ToList();
        var rightAfter = world.SourceDigest(keys);
        await world.WaitFrames(10);

        AssertThat(op.Mode).IsEqual(TerrainEditMode.Remove);
        AssertThat(keys.Count).IsEqual(8);
        AssertThat(world.LiveDigest()).IsEqual(rightAfter);
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Edit_SameOpsDifferentLoadHistory_IdenticalDigest()
    {
        var edits = RandomEdits(80, 5, 20);
        var area = TerrainTestWorld.Ground(0, 0);
        ulong generatorOnly;
        await using (var scene = EngineTestScene.Create())
        {
            await using var world = await TerrainTestWorld.CreateAsync(scene);
            world.AddViewer(area, 96);
            AssertThat(await world.WaitLoaded(area, 48)).IsTrue();
            foreach (var (center, radius, mode) in edits)
            {
                world.Edit(center, radius, mode);
            }

            generatorOnly = world.LiveDigest();
        }

        await using (var scene = EngineTestScene.Create())
        {
            await using var world = await TerrainTestWorld.CreateAsync(scene);
            var viewer = world.AddViewer(area, 96);
            AssertThat(await world.WaitLoaded(area, 48)).IsTrue();
            for (var i = 0; i < edits.Count; i++)
            {
                if (i == edits.Count / 2)
                {
                    // Unload every edited block and load it again through the stream before the second half.
                    viewer.Position = TerrainVectors.ToGodot(new Vector3(3000, S, 0));
                    AssertThat(await world.WaitUnloaded(area, 48)).IsTrue();
                    viewer.Position = TerrainVectors.ToGodot(area);
                    AssertThat(await world.WaitLoaded(area, 48)).IsTrue();
                    AssertThat(world.Node.Stream.SourceReads).IsGreater(0);
                }

                world.Edit(edits[i].Center, edits[i].Radius, edits[i].Mode);
            }

            AssertThat(world.LiveDigest()).IsEqual(generatorOnly);
            AssertThat(world.Node.Stream.SaveMismatches).IsEqual(0);
            AssertThat(world.Node.Stream.ErrorCount).IsEqual(0);
        }
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task ApplyEdit_AreaNotEditable_ThrowsBeforeChange()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        // Loads only a small area around x = 0; the edit at x = 300 is outside it.
        world.AddViewer(TerrainTestWorld.Ground(0, 0), 32);
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 8)).IsTrue();
        var near = TerrainTestWorld.Ground(0, 0);
        using var block = new VoxelBuffer();
        var key = new TerrainBlockKey(WorldFactory.HomePlanet, 0, 0, (int)(S / 16), 0);
        AssertThat(world.Service.TryCopyBlock(key, block)).IsTrue();
        var nearDigest = world.Codec.Digest(block);
        var far = new Vector3(300, S, 0);
        var keys = new List<TerrainBlockKey>();
        TerrainMath.AffectedBlocks(WorldFactory.HomePlanet, far, 2f, 1f, TerrainRules.EditMarginVoxels, keys);
        var versions = keys.Select(k => new TerrainBlockVersion(k, 1)).ToList();

        AssertThrown(() => world.Service.ApplyEdit(world.EditOp(far, 2f, TerrainEditMode.Remove), versions)).IsInstanceOf<InvalidOperationException>();
        // A version list that does not match the affected blocks is refused too, even where the area is loaded.
        AssertThrown(() => world.Service.ApplyEdit(world.EditOp(near, 2f, TerrainEditMode.Remove), versions)).IsInstanceOf<InvalidOperationException>();

        AssertThat(world.Blocks.PendingCount).IsEqual(0);
        AssertThat(world.Service.TryCopyBlock(key, block)).IsTrue();
        AssertThat(world.Codec.Digest(block)).IsEqual(nearDigest);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task IsAreaLoaded_Saturated_ReturnsFalse()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene, new TerrainBufferOptions { EarlyCheckpointBlocks = 4, MaxPendingBlocks = 8 });
        var player = world.Sim.ApplyServerOperation(world.Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, 1), "digger"));
        var join = (PlayerJoinOp)player.Operation;
        world.AddViewer(TerrainTestWorld.Ground(0, 0));
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 24)).IsTrue();
        var eye = join.Position + new Vector3(0, world.Sim.Rules.EyeHeight, 0);
        var dig = new TerrainToolIntent(WorldFactory.HomePlanet, eye, -Vector3.UnitY, 1f, TerrainEditMode.Remove, 0);

        var first = world.Sim.Submit(join.Player, dig);
        AssertThat(first.IsAccepted).IsTrue();
        world.Edit(new Vector3(8, S, 8), 3f, TerrainEditMode.Remove);
        AssertThat(world.Blocks.IsSaturated).IsTrue();
        world.Sim.AdvanceTick();
        var refused = world.Sim.Submit(join.Player, dig);

        AssertThat(refused.Reason).IsEqual(RejectReason.AreaNotLoaded);
        AssertThat(world.Service.IsAreaLoaded(WorldFactory.HomePlanet, eye, 1f)).IsFalse();
        AssertThat(await world.CheckpointAndPump()).IsTrue();
        AssertThat(world.Service.IsAreaLoaded(WorldFactory.HomePlanet, join.Position, 4f)).IsTrue();
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Raycast_NoManagedAllocation()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        world.AddViewer(TerrainTestWorld.Ground(0, 0));
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 24)).IsTrue();
        var origin = new Vector3(1.3f, S + 5.2f, -2.7f);
        var direction = Vector3.Normalize(new Vector3(0.3f, -1f, 0.1f));
        for (var i = 0; i < 20; i++)
        {
            world.Service.Raycast(WorldFactory.HomePlanet, origin, direction, 8f, out _);
        }

        var hits = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            if (world.Service.Raycast(WorldFactory.HomePlanet, origin, direction, 8f, out _))
            {
                hits++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertThat(hits).IsEqual(1000);
        AssertThat(allocated).IsEqual(0L);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Raycast_FlatGround_PreciseHitAndNormal()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        world.AddViewer(TerrainTestWorld.Ground(0, 0));
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 24)).IsTrue();

        var hit = world.Service.Raycast(WorldFactory.HomePlanet, new Vector3(1.3f, S + 5.2f, -2.7f), -Vector3.UnitY, 8f, out var down);
        var miss = world.Service.Raycast(WorldFactory.HomePlanet, new Vector3(1.3f, S + 5.2f, -2.7f), Vector3.UnitY, 8f, out _);
        var tooShort = world.Service.Raycast(WorldFactory.HomePlanet, new Vector3(1.3f, S + 5.2f, -2.7f), -Vector3.UnitY, 5f, out _);

        AssertThat(hit).IsTrue();
        AssertThat(Math.Abs(down.Position.Y - S)).IsLess(0.01f);
        AssertThat(Math.Abs(down.Distance - 5.2f)).IsLess(0.01f);
        AssertThat(down.Normal.Y).IsGreater(0.999f);
        AssertThat(miss).IsFalse();
        AssertThat(tooShort).IsFalse();
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Raycast_ThinSolidBetweenSamples_Hits()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        world.AddViewer(TerrainTestWorld.Ground(0, 0));
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 24)).IsTrue();
        var tool = world.Node.Tool;
        tool.Channel = TerrainChannels.Sdf;
        var y = (int)S + 10;
        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dy = -1; dy <= 2; dy++)
            {
                for (var dz = -1; dz <= 2; dz++)
                {
                    tool.SetVoxelF(new Vector3I(dx, y + dy, dz), 1f);
                }
            }
        }

        // One solid voxel with air (+1) around it: along y = z = 0.25 the solid part is -1/9 < x < 1/9, between samples 0.5 m apart.
        tool.SetVoxelF(new Vector3I(0, y, 0), -1f);

        var hit = world.Service.Raycast(WorldFactory.HomePlanet, new Vector3(-0.25f, y + 0.25f, 0.25f), Vector3.UnitX, 1f, out var thin);

        AssertThat(hit).IsTrue();
        AssertThat(Math.Abs(thin.Position.X + (1f / 9f))).IsLess(0.01f);
        AssertThat(Math.Abs(thin.Distance - (0.25f - (1f / 9f)))).IsLess(0.01f);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task TerrainService_OtherThread_Throws()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);

        var error = await Task.Run(() =>
        {
            try
            {
                world.Service.IsAreaLoaded(WorldFactory.HomePlanet, TerrainTestWorld.Ground(0, 0), 1f);
                return null;
            }
            catch (InvalidOperationException exception)
            {
                return exception;
            }
        });

        AssertThat(error is not null).IsTrue();
    }
}
