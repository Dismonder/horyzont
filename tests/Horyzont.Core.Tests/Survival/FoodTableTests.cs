using Horyzont.Core.Ids;
using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>The food table built from a lookup, as the client builds it from the content of its items (K6-33 D21).</summary>
public sealed class FoodTableTests
{
    [Fact]
    public void Build_FromLookup_FindsOnlyTheFoodTypes()
    {
        var table = FoodTable.Build(type => type.Value switch
        {
            5 => FoodContent.Berries,
            4096 => FoodContent.Jam,
            _ => null,
        });

        Assert.True(table.TryGet(new ItemTypeId(5), out var berries));
        Assert.Same(FoodContent.Berries, berries);
        Assert.True(table.TryGet(new ItemTypeId(4096), out var jam));
        Assert.Same(FoodContent.Jam, jam);
        Assert.False(table.TryGet(new ItemTypeId(6), out _));
        Assert.False(table.TryGet(new ItemTypeId(0), out _));
        Assert.False(table.TryGet(new ItemTypeId(4097), out _));
    }

    [Fact]
    public void Build_FromLookup_NullLookup_Throws() =>
        Assert.Throws<ArgumentNullException>(() => FoodTable.Build((Func<ItemTypeId, Horyzont.Core.Content.FoodDef?>)null!));
}
