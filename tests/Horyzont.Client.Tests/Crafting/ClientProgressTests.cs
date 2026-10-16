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

        progress.Apply(new PlayerProgressState(Bits(3, 5), ["a", "b"], FoodSlots.Empty));

        Assert.Equal(Bits(3, 5), progress.Discovered);
        Assert.Equal(["a", "b"], progress.WorldKeys.Order(StringComparer.Ordinal));
        Assert.Equal(1, changed);

        progress.Apply(new PlayerProgressState(Bits(7), ["c"], FoodSlots.Empty));

        Assert.Equal(Bits(7), progress.Discovered);
        Assert.False(progress.Discovered.Contains(new ItemTypeId(3)));
        Assert.Equal(["c"], progress.WorldKeys);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void ProgressState_FoodApplied()
    {
        var progress = new ClientProgress();
        Assert.True(progress.Food.IsEmpty);
        var food = FoodSlots.Empty.With(0, new FoodSlot(new ItemTypeId(5), new Tick(100))).With(2, new FoodSlot(new ItemTypeId(7), new Tick(250)));

        progress.Apply(new PlayerProgressState(Bits(3), ["a"], food));

        Assert.Equal(food, progress.Food);
        Assert.Equal(new ItemTypeId(7), progress.Food[2].Type);

        progress.Apply(new PlayerProgressState(Bits(3), ["a"], FoodSlots.Empty));
        Assert.True(progress.Food.IsEmpty);

        progress.Apply(new PlayerProgressState(Bits(3), ["a"], food));
        progress.Clear();
        Assert.True(progress.Food.IsEmpty);
    }

    [Fact]
    public void Disconnect_Clears()
    {
        var progress = new ClientProgress();
        progress.Apply(new PlayerProgressState(Bits(3), ["a"], FoodSlots.Empty));
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

        replication.OnMessage(new PlayerProgressState(Bits(4), ["k"], FoodSlots.Empty));

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

        replication.OnMessage(new PlayerProgressState(Bits(4), ["k"], FoodSlots.Empty));

        Assert.Null(replication.Progress);
    }
}
