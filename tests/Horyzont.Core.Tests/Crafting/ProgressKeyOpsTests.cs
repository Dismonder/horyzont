using System.Globalization;

using Horyzont.Core.Items;
using Horyzont.Core.Operations;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>K6-32, contract of operation 45 (<see cref="ProgressKeyGrantOp"/>). Every refusal starts from the accepted counterpart.</summary>
public sealed class ProgressKeyOpsTests
{
    [Fact]
    public void Grant_NewKey_Added()
    {
        var f = new CraftingFixture();
        Assert.Equal(0, f.State.ProgressKeys.Count);
        Assert.False(f.State.ProgressKeys.Contains("test:boss"));

        f.Apply(CraftingFixture.Grant("test:boss"));
        f.Apply(CraftingFixture.Grant("test:alpha"));

        Assert.True(f.State.ProgressKeys.Contains("test:boss"));
        Assert.Equal(2, f.State.ProgressKeys.Count);
        Assert.Equal(["test:alpha", "test:boss"], f.State.ProgressKeys.Sorted);
    }

    [Fact]
    public void Grant_ExistingKey_Fails()
    {
        var f = new CraftingFixture();
        f.Apply(CraftingFixture.Grant("test:boss"));
        f.Apply(CraftingFixture.Grant("test:other"));

        f.AssertFails(CraftingFixture.Grant("test:boss"));
        Assert.Equal(2, f.State.ProgressKeys.Count);
    }

    [Fact]
    public void Grant_EmptyOrOver64Bytes_Fails()
    {
        var f = new CraftingFixture();

        // 64 bytes are accepted, also as 32 two-byte characters; 65 bytes and the empty key are refused.
        f.Apply(CraftingFixture.Grant(new string('k', 64)));
        f.Apply(CraftingFixture.Grant(new string('ą', 32)));
        f.AssertFails(CraftingFixture.Grant(new string('k', 65)));
        f.AssertFails(CraftingFixture.Grant(new string('ą', 33)));
        f.AssertFails(CraftingFixture.Grant(string.Empty));
        Assert.Equal(2, f.State.ProgressKeys.Count);
    }

    [Fact]
    public void Grant_LoneSurrogate_Fails()
    {
        var f = new CraftingFixture();

        // The accepted counterpart: a valid surrogate pair. The key must be strict UTF-8, the same as ByteWriter writes, or the journal could not store the operation.
        f.Apply(CraftingFixture.Grant("t:\U0001F600"));
        f.AssertFails(CraftingFixture.Grant("t:\uD800"));
        f.AssertFails(CraftingFixture.Grant("t:\uDC00x"));
        Assert.Equal(1, f.State.ProgressKeys.Count);
    }

    [Fact]
    public void Grant_At256Keys_Fails()
    {
        var f = new CraftingFixture();
        for (var i = 0; i < 256; i++)
        {
            f.Apply(CraftingFixture.Grant("k" + i.ToString("D3", CultureInfo.InvariantCulture)));
        }

        Assert.Equal(256, f.State.ProgressKeys.Count);
        f.AssertFails(CraftingFixture.Grant("k256"));
        Assert.Equal(256, f.State.ProgressKeys.Count);
    }

    [Fact]
    public void Grant_InCompoundRolledBack_KeyAbsent()
    {
        var f = new CraftingFixture();
        var create = new ItemCreateOp(new SlotRef(Bag, 0), CraftingFixture.Stone, 1, 500, ItemSource.Admin);
        var destroy = new ItemDestroyOp(new SlotRef(Bag, 0), CraftingFixture.Stone, 1, ItemSink.Craft);
        var grant = CraftingFixture.Grant("test:boss");

        // The accepted counterpart keeps the key.
        f.Apply(new CompoundOp([create, grant, destroy]));
        Assert.True(f.State.ProgressKeys.Contains("test:boss"));

        var g = new CraftingFixture();
        g.AssertFails(new CompoundOp([grant, destroy]));
        Assert.False(g.State.ProgressKeys.Contains("test:boss"));
        Assert.Equal(0, g.State.ProgressKeys.Count);
    }
}
