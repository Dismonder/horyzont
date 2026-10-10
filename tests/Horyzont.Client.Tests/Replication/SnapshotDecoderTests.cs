using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Tests.Replication;

public sealed class SnapshotDecoderTests
{
    [Fact]
    public void Snapshot_AcknowledgedBaselineAndDroppedPackets_ReconstructsFullState()
    {
        var decoder = new SnapshotDecoder();
        SnapshotFrame[] frames = [new(FrameRef.Of(new PlanetId(1)), 0)];
        var entity = new EntityState(new EntityId(1), (EntityStateFields)15, 0, 0, Vector3.One, Quaternion.Identity, Vector3.UnitX, 1);
        Assert.True(decoder.TryReconstruct(new Snapshot(uint.MaxValue, new Tick(1), 0, 2, frames, [entity]), out _));
        var delta = entity with { Fields = EntityStateFields.Position, Position = Vector3.One * 2 };
        Assert.True(decoder.TryReconstruct(new Snapshot(1, new Tick(2), uint.MaxValue, 3, frames, [delta]), out var full));
        Assert.Equal(0u, full.BaselineSnapshotId);
        Assert.Equal(Vector3.One * 2, full.Entities[0].Position);
        Assert.Equal(Vector3.UnitX, full.Entities[0].Velocity);
        Assert.Equal((EntityStateFields)15, full.Entities[0].Fields);
        Assert.False(decoder.TryReconstruct(new Snapshot(2, new Tick(3), 99, 4, frames, [delta]), out _));
        Assert.True(decoder.TryReconstruct(new Snapshot(3, new Tick(4), 1, 4, frames, []), out full));
        Assert.Empty(full.Entities);
        Assert.False(decoder.TryReconstruct(new Snapshot(4, new Tick(5), 3, 5, frames, [delta]), out _));
        for (uint i = 5; i < 70; i++)
        {
            Assert.True(decoder.TryReconstruct(new Snapshot(i, new Tick(i), 0, i, frames, [entity]), out _));
        }

        Assert.False(decoder.TryReconstruct(new Snapshot(70, new Tick(70), 1, 70, frames, [delta]), out _));
    }
}
