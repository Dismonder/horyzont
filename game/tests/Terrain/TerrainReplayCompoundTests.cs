using GdUnit4;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Tests.Support;
using Horyzont.Persistence;
using Horyzont.Testing;

using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

/// <summary>A journal tail whose terrain edit sits inside a compound operation replays like a plain edit (K6-29W stage C).</summary>
[TestSuite]
public class TerrainReplayCompoundTests
{
    private static readonly Vector3 _area = TerrainTestWorld.Ground(0, 0);

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Replay_CompoundWithDig_LoadsTheAreaBeforeTheEdit()
    {
        await using var scene = EngineTestScene.Create();
        var time = new FakeTimeProvider();
        var path = TerrainTestWorld.NewPath();
        var world = WorldState.CreateNew(
            WorldFactory.DefaultWorldId, 42, [TerrainTestWorld.Planet], [WorldFactory.StoneBlockKey], TerrainMaterials.Keys,
            SurvivalContentFactory.ItemKeys);
        WorldCreator.Create(path, world, TerrainTestWorld.Options, time);
        ulong stateHash;
        ulong digest;
        List<TerrainBlockVersion> versions;
        await using (var live = await TerrainTestWorld.OpenAsync(scene, path, time: time))
        {
            live.AddViewer(_area, 96);
            AssertThat(await live.WaitLoaded(_area, 40)).IsTrue();
            var join = live.Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, 1), "player");
            live.Sim.ApplyServerOperation(join);
            live.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(join.Player, 1));
            var dig = live.EditOp(new Vector3(1f, TerrainTestWorld.Surface - 1f, 1f), 2f, TerrainEditMode.Remove);
            live.Sim.ApplyServerOperation(new CompoundOp(
            [
                new ItemCreateOp(new SlotRef(WorldFactory.BagOf(1), 0), WorldFactory.Item(SurvivalContentFactory.StoneKey), 6, 500, ItemSource.Harvest),
                dig,
            ]));
            AssertThat(await live.Writer.FlushAsync()).IsTrue();
            stateHash = StateHasher.Hash(live.State);
            digest = live.LiveDigest();
            versions = live.State.TerrainVersions.All.ToList();
            AssertThat(versions.Count).IsGreater(0);
        }

        await using var restarted = await TerrainTestWorld.OpenAsync(scene, path);

        AssertThat(restarted.TailCheck!.ReplayAllowed).IsTrue();
        AssertThat(restarted.TailCheck.TerrainEditCount).IsEqual(1);
        restarted.AddViewer(_area, 96);
        AssertThat(await restarted.WaitLoaded(_area, 40)).IsTrue();
        AssertThat(StateHasher.Hash(restarted.State)).IsEqual(stateHash);
        AssertThat(restarted.State.TerrainVersions.All.ToList()).IsEqual(versions);
        AssertThat(restarted.LiveDigest()).IsEqual(digest);
    }
}
