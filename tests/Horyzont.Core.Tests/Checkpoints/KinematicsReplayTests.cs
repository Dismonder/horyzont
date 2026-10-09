using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Operations;

namespace Horyzont.Core.Tests.Checkpoints;

public sealed class KinematicsReplayTests
{
    /// <summary>
    /// A character walks (the pose reaches the simulation only through <c>UpdatePlayerKinematics</c>, outside the journal), a checkpoint is
    /// taken in the middle, then the player leaves with the last pose. The leave operation is what carries the pose into the journal, so
    /// the live state, the replay of the whole journal and the checkpoint plus its tail must have the same hash.
    /// </summary>
    [Fact]
    public void Kinematics_WalkThenLeave_LiveEqualsReplayEqualsCheckpointPlusTail()
    {
        var live = new SimFixture();
        var player = live.Join();
        var first = live.State.CaptureCheckpoint();
        live.State.CompleteCheckpoint(first);
        var checkpoints = new List<Horyzont.Core.Checkpoints.WorldCheckpoint> { first };
        var state = live.PlayerOf(player);

        var position = state.Position;
        var rotation = state.BodyRotation;
        var pitch = 0f;
        for (var tick = 0; tick < 120; tick++)
        {
            position += new Vector3(0.05f, 0f, 0.02f);
            rotation = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(tick * 0.01f, 0f, 0f));
            pitch = tick * 0.002f;
            Assert.True(live.Sim.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, position, rotation, pitch));
            live.Ticks(1);
            if (tick == 60)
            {
                var middle = live.State.CaptureCheckpoint();
                live.State.CompleteCheckpoint(middle);
                checkpoints.Add(middle);
            }
        }

        live.Sim.ApplyServerOperation(new PlayerLeaveOp(player, state.Frame, state.ParentSeq, position, rotation, pitch));
        live.Ticks(5);

        var replay = new SimFixture();
        foreach (var entry in live.Journal.Entries)
        {
            replay.Sim.Replay(entry);
        }

        var barrier = checkpoints[^1].BarrierOpId;
        var fromCheckpoint = new SimFixture(state: CheckpointTests.Load(live.State, checkpoints));
        foreach (var entry in live.Journal.Entries.Where(e => e.Id > barrier))
        {
            fromCheckpoint.Sim.Replay(entry);
        }

        Assert.Contains(live.Journal.Entries, e => e.Operation is PlayerLeaveOp && e.Id > barrier);
        Assert.Equal(position, live.PlayerOf(player).Position);
        Assert.Equal(StateHasher.Hash(live.State), StateHasher.Hash(replay.State));
        Assert.Equal(StateHasher.Hash(live.State), StateHasher.Hash(fromCheckpoint.State));
    }
}
