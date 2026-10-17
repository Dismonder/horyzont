using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-34, D4 and D10: the five resources of the suit and the one-to-one mapping of the hazards to them.</summary>
public sealed class SuitResourcesTests
{
    private static readonly SuitResources _distinct = new(1f, 2f, 3f, 4f, 5f);

    [Fact]
    public void Indexer_MapsEachResource()
    {
        Assert.Equal(1f, _distinct[SuitResource.Heating]);
        Assert.Equal(2f, _distinct[SuitResource.Cooling]);
        Assert.Equal(3f, _distinct[SuitResource.Filter]);
        Assert.Equal(4f, _distinct[SuitResource.Shield]);
        Assert.Equal(5f, _distinct[SuitResource.Oxygen]);

        Assert.Throws<ArgumentOutOfRangeException>(() => _distinct[(SuitResource)0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _distinct[(SuitResource)6]);
    }

    [Fact]
    public void With_ReplacesOne()
    {
        foreach (var resource in Enum.GetValues<SuitResource>())
        {
            var replaced = _distinct.With(resource, 77f);

            Assert.Equal(77f, replaced[resource]);
            foreach (var other in Enum.GetValues<SuitResource>().Where(r => r != resource))
            {
                Assert.Equal(_distinct[other], replaced[other]);
            }
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => _distinct.With((SuitResource)0, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => _distinct.With((SuitResource)6, 1f));
    }

    [Fact]
    public void IsValid_Bounds()
    {
        // The limits are valid: 0 and 1000 (ProtocolConstants.MaxSuitValue).
        Assert.True(new SuitResources(0f, 0f, 0f, 0f, 0f).IsValid);
        Assert.True(new SuitResources(1000f, 1000f, 1000f, 1000f, 1000f).IsValid);
        Assert.True(_distinct.IsValid);
        Assert.True(new SuitResources(-0f, 0f, 0f, 0f, 0f).IsValid);

        foreach (var bad in new[] { -1f, -0.001f, 1000.01f, 1001f, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            foreach (var resource in Enum.GetValues<SuitResource>())
            {
                Assert.False(_distinct.With(resource, bad).IsValid, $"{resource} = {bad}");
            }
        }
    }

    [Fact]
    public void HazardToResource_OneToOne()
    {
        // Cold drains the heating, heat the cooling, toxins the filter, radiation the shield, vacuum the oxygen (D10): the bytes are the same 1..5.
        Assert.Equal((byte)SuitResource.Heating, (byte)HazardKind.Cold);
        Assert.Equal((byte)SuitResource.Cooling, (byte)HazardKind.Heat);
        Assert.Equal((byte)SuitResource.Filter, (byte)HazardKind.Toxins);
        Assert.Equal((byte)SuitResource.Shield, (byte)HazardKind.Radiation);
        Assert.Equal((byte)SuitResource.Oxygen, (byte)HazardKind.Vacuum);
        Assert.Equal([1, 2, 3, 4, 5], Enum.GetValues<HazardKind>().Select(k => (int)k));
        Assert.Equal([1, 2, 3, 4, 5], Enum.GetValues<SuitResource>().Select(r => (int)r));

        // The rates are indexed by the hazard in the same order.
        var rates = new HazardRates(1f, 2f, 3f, 4f, 5f);
        foreach (var kind in Enum.GetValues<HazardKind>())
        {
            Assert.Equal(_distinct[(SuitResource)(byte)kind], rates[kind]);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => rates[(HazardKind)0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => rates[(HazardKind)6]);
    }
}
