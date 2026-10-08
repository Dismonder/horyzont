using System.Numerics;

using FsCheck.Xunit;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Checkpoints;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Tests.Hashing;

public sealed class DeterminismTests
{
    /// <summary>
    /// Intents plus the operations the server issues around them: leave, re-join, reparent into a seat and back, and a checkpoint
    /// in the middle of the sequence. Every checkpoint taken is appended to <paramref name="checkpoints"/>.
    /// </summary>
    private static void Play(SimFixture f, PlayerId p, int[] steps, List<WorldCheckpoint>? checkpoints = null)
    {
        foreach (var value in steps.Take(40))
        {
            var kind = (int)((uint)value % 9);
            var a = (int)((uint)value / 9 % 9) - 4;
            var player = f.PlayerOf(p);
            switch (kind)
            {
                case 0:
                    f.Sim.Submit(p, f.Dig(p, f.Ground(a + 0.5f, 1.5f)));
                    break;
                case 1:
                    f.Sim.Submit(p, f.PlaceOnTerrain(p, f.Ground(a, 3)));
                    break;
                case 2:
                    f.Sim.Submit(p, f.Fill(p, f.Ground(a, -5), material: 2));
                    break;
                case 3 when f.State.Constructs.Count > 0:
                    f.Sim.Submit(p, new PlaceBlockIntent(
                        new ConstructFaceTarget(f.State.Constructs.All[0].Id, f.State.Constructs.All[0].BoundsMax, (Face)((a + 4) % 6)), f.Stone, (byte)(a + 4)));
                    break;
                case 4 when f.State.Constructs.Count > 0:
                    f.Sim.Submit(p, new RemoveBlockIntent(f.State.Constructs.All[^1].Id, f.State.Constructs.All[^1].BoundsMin));
                    break;
                case 5 when player.IsOnline:
                    f.Sim.ApplyServerOperation(new PlayerLeaveOp(
                        p, player.Frame, player.ParentSeq, new Vector3(a, 4000 + (a * 0.25f), 1.5f),
                        Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(a * 0.3f, 0, 0)), a * 0.1f));
                    break;
                case 6 when !player.IsOnline:
                    f.Sim.ApplyServerOperation(f.Sim.PreparePlayerJoin(player.Identity, "again"));
                    break;
                case 7 when player.IsOnline && f.State.Constructs.Count > 0:
                    Reparent(f, player, f.State.Constructs.All[0].Id, a);
                    break;
                case 8:
                    var checkpoint = f.State.CaptureCheckpoint();
                    f.State.CompleteCheckpoint(checkpoint);
                    checkpoints?.Add(checkpoint);
                    break;
                default:
                    f.Sim.Submit(p, f.Dig(p, f.Ground(a, -2)));
                    break;
            }

            f.Ticks(7);
        }
    }

    /// <summary>Boards the construct (seat at its origin cell) or, when already seated, returns to the planet.</summary>
    private static void Reparent(SimFixture f, PlayerState player, EntityId construct, int a)
    {
        var operation = player.Frame.Kind == FrameKind.Planet
            ? new EntityReparentOp(player.Entity, FrameRef.Of(construct), player.ParentSeq + 1, new Vector3(0.5f, 1f, 0.5f), Quaternion.Identity, Vector3.Zero, new CellPos(0, 0, 0))
            : new EntityReparentOp(player.Entity, FrameRef.Of(SimFixture.Planet), player.ParentSeq + 1, f.Ground(a) + Vector3.UnitY, Quaternion.Identity, Vector3.Zero, null);
        try
        {
            f.Sim.ApplyServerOperation(operation);
        }
        catch (InvalidOperationException)
        {
            // The construct or its seat cell is gone: the operation is not applicable and nothing changed.
        }
    }

    private static (ulong Live, ulong Second, ulong Replay, ulong FromCheckpoint, SimFixture First, List<WorldCheckpoint> Checkpoints) Run(int[] steps)
    {
        var first = new SimFixture();
        var second = new SimFixture();
        var p1 = first.Join();
        var p2 = second.Join();
        var checkpoints = new List<WorldCheckpoint>();
        checkpoints.Add(first.State.CaptureCheckpoint());
        first.State.CompleteCheckpoint(checkpoints[0]);

        Play(first, p1, steps, checkpoints);
        Play(second, p2, steps);

        var replay = new SimFixture();
        foreach (var entry in first.Journal.Entries)
        {
            replay.Sim.Replay(entry);
        }

        var fromCheckpoint = new SimFixture(state: CheckpointTests.Load(first.State, checkpoints));
        foreach (var entry in first.Journal.Entries.Where(e => e.Id > checkpoints[^1].BarrierOpId))
        {
            fromCheckpoint.Sim.Replay(entry);
        }

        return (StateHasher.Hash(first.State), StateHasher.Hash(second.State), StateHasher.Hash(replay.State), StateHasher.Hash(fromCheckpoint.State), first, checkpoints);
    }

    [Property(MaxTest = 25)]
    public void SameIntents_TwoFreshWorlds_SameHashAndReplayAndCheckpointTailAgree(int[] steps)
    {
        var run = Run(steps ?? []);

        Assert.Equal(run.Live, run.Second);
        Assert.Equal(run.Live, run.Replay);
        Assert.Equal(run.Live, run.FromCheckpoint);
    }

    [Fact]
    public void LeaveRejoinReparentAndMidCheckpoint_LiveReplayAndCheckpointTailAgree()
    {
        // Fixed script that exercises every kind of step (kind = value % 9, offset a = value / 9 % 9 - 4).
        int[] steps = [1, 7, 0, 5 + (9 * 6), 8, 7, 5 + (9 * 2), 6, 0, 7, 8, 5 + (9 * 8), 3, 6, 7, 5, 6, 0];

        var run = Run(steps);

        var journal = run.First.Journal.Entries;
        Assert.Contains(journal, e => e.Operation is PlayerLeaveOp);
        Assert.Contains(journal, e => e.Operation is PlayerJoinOp && e.Id.Value > 1);
        Assert.Contains(journal, e => e.Operation is EntityReparentOp);
        Assert.True(run.Checkpoints.Count >= 3);
        Assert.True(run.Checkpoints[^1].BarrierOpId < run.First.State.LastOpId, "the last checkpoint must leave a journal tail");
        Assert.Equal(run.Live, run.Replay);
        Assert.Equal(run.Live, run.FromCheckpoint);
    }

    [Fact]
    public void Hash_OfflinePlayerPose_Changes()
    {
        var (f, p, _, _) = Arrange();
        var player = f.PlayerOf(p);
        f.Sim.ApplyServerOperation(new PlayerLeaveOp(p, player.Frame, player.ParentSeq, player.Position, Quaternion.Identity, 0f));
        var offline = StateHasher.Hash(f.State);

        player.Position += new Vector3(0.25f, 0, 0);
        var moved = StateHasher.Hash(f.State);
        player.BodyRotation = Quaternion.CreateFromYawPitchRoll(0.5f, 0, 0);
        var rotated = StateHasher.Hash(f.State);
        player.Pitch = 0.3f;
        var pitched = StateHasher.Hash(f.State);

        Assert.Equal(4, new[] { offline, moved, rotated, pitched }.Distinct().Count());
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
