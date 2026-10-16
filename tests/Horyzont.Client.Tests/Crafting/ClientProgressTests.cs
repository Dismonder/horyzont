using Horyzont.Client.Crafting;
using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Tests.Crafting;

public sealed class ClientProgressTests
{
    private static ItemTypeBitSet Bits(params ushort[] types) => types.Aggregate(ItemTypeBitSet.Empty, (set, t) => set.With(new ItemTypeId(t)));

    [Fact]
    public void State_Replaces()
    {
        var progress = new ClientProgress();
        var changed = 0;
        progress.Changed += () => changed++;
        Assert.True(progress.Discovered.IsEmpty);
        Assert.Empty(progress.WorldKeys);

        progress.Apply(new PlayerProgressState(Bits(3, 5), ["a", "b"]));

        Assert.Equal(Bits(3, 5), progress.Discovered);
        Assert.Equal(["a", "b"], progress.WorldKeys.Order(StringComparer.Ordinal));
        Assert.Equal(1, changed);

        progress.Apply(new PlayerProgressState(Bits(7), ["c"]));

        Assert.Equal(Bits(7), progress.Discovered);
        Assert.False(progress.Discovered.Contains(new ItemTypeId(3)));
        Assert.Equal(["c"], progress.WorldKeys);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Disconnect_Clears()
    {
        var progress = new ClientProgress();
        progress.Apply(new PlayerProgressState(Bits(3), ["a"]));
        var changed = 0;
        progress.Changed += () => changed++;

        progress.Clear();

        Assert.True(progress.Discovered.IsEmpty);
        Assert.Empty(progress.WorldKeys);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Replication_ProgressState_ReachesClientProgress_DisconnectClearsIt()
    {
        var progress = new ClientProgress();
        var replication = new ClientReplication(_ => { }, null, null, null, progress: progress);
        Assert.Same(progress, replication.Progress);

        replication.OnMessage(new PlayerProgressState(Bits(4), ["k"]));

        Assert.Equal(Bits(4), progress.Discovered);
        Assert.Equal(["k"], progress.WorldKeys);

        replication.OnDisconnected(DisconnectCode.ServerShutdown);

        Assert.True(progress.Discovered.IsEmpty);
        Assert.Empty(progress.WorldKeys);
    }

    [Fact]
    public void Replication_WithoutProgress_IgnoresTheMessage()
    {
        var replication = new ClientReplication(_ => { }, null);

        replication.OnMessage(new PlayerProgressState(Bits(4), ["k"]));

        Assert.Null(replication.Progress);
    }
}
