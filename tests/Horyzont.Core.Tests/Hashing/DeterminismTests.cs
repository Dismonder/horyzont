using System.Numerics;

using FsCheck.Xunit;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Tests.Hashing;

public sealed class DeterminismTests
{
    private static void Play(SimFixture f, PlayerId p, int[] steps)
    {
        foreach (var value in steps.Take(40))
        {
            var kind = (int)((uint)value % 5);
            var a = (int)((uint)value / 5 % 9) - 4;
            Intent intent = kind switch
            {
                0 => f.Dig(p, f.Ground(a + 0.5f, 1.5f)),
                1 => f.PlaceOnTerrain(p, f.Ground(a, 3)),
                2 => f.Fill(p, f.Ground(a, -5), material: 2),
                3 when f.State.Constructs.Count > 0 => new PlaceBlockIntent(
                    new ConstructFaceTarget(f.State.Constructs.All[0].Id, f.State.Constructs.All[0].BoundsMax, (Face)((a + 4) % 6)), f.Stone, (byte)(a + 4)),
                4 when f.State.Constructs.Count > 0 => new RemoveBlockIntent(f.State.Constructs.All[^1].Id, f.State.Constructs.All[^1].BoundsMin),
                _ => f.Dig(p, f.Ground(a, -2)),
            };
            f.Sim.Submit(p, intent);
            f.Ticks(7);
        }
    }

    [Property(MaxTest = 25)]
    public void SameIntents_TwoFreshWorlds_SameHashAndReplayAgrees(int[] steps)
    {
        steps ??= [];
        var first = new SimFixture();
        var second = new SimFixture();
        var p1 = first.Join();
        var p2 = second.Join();

        Play(first, p1, steps);
        Play(second, p2, steps);

        var replay = new SimFixture();
        foreach (var entry in first.Journal.Entries)
        {
            replay.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(first.State), StateHasher.Hash(second.State));
        Assert.Equal(StateHasher.Hash(first.State), StateHasher.Hash(replay.State));
    }

    private static (SimFixture F, PlayerId P, EntityId C, ulong Hash) Arrange()
    {
        var f = new SimFixture();
        var p = f.Join();
        var c = f.BuildOnTerrain(p, 2);
        f.Sim.Submit(p, f.Dig(p, f.Ground(-2)));
        return (f, p, c, StateHasher.Hash(f.State));
    }

    [Fact]
    public void Hash_ConstructCell_Changes()
    {
        var (f, _, c, hash) = Arrange();
        f.State.Constructs.TryGet(c, out var construct);

        construct!.SetBlock(new CellPos(0, 1, 0), BlockState.Create(f.Stone, 0));

        Assert.NotEqual(hash, StateHasher.Hash(f.State));
    }

    [Fact]
    public void Hash_TerrainVersion_Changes()
    {
        var (f, _, _, hash) = Arrange();

        f.State.TerrainVersions.Bump(new TerrainBlockKey(SimFixture.Planet, 0, 1, 2, 3));

        Assert.NotEqual(hash, StateHasher.Hash(f.State));
    }

    [Fact]
    public void Hash_IsOnline_Changes()
    {
        var (f, p, _, hash) = Arrange();

        f.PlayerOf(p).IsOnline = false;

        Assert.NotEqual(hash, StateHasher.Hash(f.State));
    }

    [Fact]
    public void Hash_PlayerFrameOrParentSeq_Changes()
    {
        var (f, p, c, hash) = Arrange();

        f.PlayerOf(p).Frame = FrameRef.Of(c);
        var frameHash = StateHasher.Hash(f.State);
        f.PlayerOf(p).ParentSeq = 1;

        Assert.NotEqual(hash, frameHash);
        Assert.NotEqual(frameHash, StateHasher.Hash(f.State));
    }

    [Fact]
    public void Hash_ConstructParentSeq_Changes()
    {
        var (f, _, c, hash) = Arrange();
        f.State.Constructs.TryGet(c, out var construct);

        construct!.Reparent(construct.Parent, 1, construct.Origin, construct.Rotation);

        Assert.NotEqual(hash, StateHasher.Hash(f.State));
    }

    [Fact]
    public void Hash_LastOpId_Changes()
    {
        var (f, _, _, hash) = Arrange();

        f.State.SetLastOpId(f.State.LastOpId.Next);

        Assert.NotEqual(hash, StateHasher.Hash(f.State));
    }

    [Fact]
    public void Hash_TickAndKinematics_DoNotChange()
    {
        var (f, p, _, hash) = Arrange();

        f.Ticks(10);
        f.MovePlayer(p, f.PlayerOf(p).Position + new Vector3(0.5f, 0, 0));

        Assert.Equal(hash, StateHasher.Hash(f.State));
    }
}
