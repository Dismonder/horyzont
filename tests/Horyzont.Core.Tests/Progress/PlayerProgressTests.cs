using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Progress;

/// <summary>K6-33, D4: <see cref="PlayerProgress.IsEmpty"/> is the one test for a record that must not exist.</summary>
public sealed class PlayerProgressTests
{
    private static readonly PlayerId _player = new(4);
    private static readonly BedRef _bed = new(new EntityId(5), new CellPos(1, -1, 0), 3);
    private static readonly FoodSlots _food = new(new FoodSlot(new ItemTypeId(5), new Tick(100)), default, default);

    [Fact]
    public void IsEmpty_OnlyWhenNoBedNoDiscoveriesNoFood()
    {
        Assert.True(new PlayerProgress(_player, null).IsEmpty);
        Assert.True(new PlayerProgress(_player, null, null, FoodSlots.Empty).IsEmpty);

        // An empty set of discoveries is normalized to none (K6-32), so it is empty too.
        Assert.True(new PlayerProgress(_player, null, ItemTypeBitSet.Empty).IsEmpty);

        Assert.False(new PlayerProgress(_player, _bed).IsEmpty);
        Assert.False(new PlayerProgress(_player, null, ItemTypeBitSet.Empty.With(new ItemTypeId(5))).IsEmpty);
        Assert.False(new PlayerProgress(_player, null, null, _food).IsEmpty);
        Assert.False(new PlayerProgress(_player, _bed, ItemTypeBitSet.Empty.With(new ItemTypeId(5)), _food).IsEmpty);
    }

    [Fact]
    public void Food_DefaultIsEmptyAndIsPartOfEquality()
    {
        var plain = new PlayerProgress(_player, _bed);

        Assert.True(plain.Food.IsEmpty);
        Assert.Equal(plain, new PlayerProgress(_player, _bed, null, FoodSlots.Empty));
        Assert.NotEqual(plain, plain with { Food = _food });
        Assert.Equal(_food, (plain with { Food = _food }).Food);
        Assert.Equal(_bed, (plain with { Food = _food }).RespawnBed);
    }
}
