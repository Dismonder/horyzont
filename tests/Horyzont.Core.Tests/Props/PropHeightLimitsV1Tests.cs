using Horyzont.Core.Props;

namespace Horyzont.Core.Tests.Props;

/// <summary>The mirror of the height bands of <see cref="PropLayoutV1"/> is the tight bound: the layout accepts the limit and nothing beyond it.</summary>
public sealed class PropHeightLimitsV1Tests
{
    private static IEnumerable<PropKindV1> Kinds => Enum.GetValues<PropKindV1>().Where(k => k != PropKindV1.None);

    [Fact]
    public void Limits_NoKindAcceptsBeyond_AndSomeKindAcceptsAt()
    {
        var layout = new PropLayoutV1();

        Assert.DoesNotContain(Kinds, k => layout.Accepts(k, PropHeightLimitsV1.MaxAboveRadiusMeters + 0.01f));
        Assert.DoesNotContain(Kinds, k => layout.Accepts(k, -PropHeightLimitsV1.MaxBelowRadiusMeters - 0.01f));
        Assert.Contains(Kinds, k => layout.Accepts(k, PropHeightLimitsV1.MaxAboveRadiusMeters));
        Assert.Contains(Kinds, k => layout.Accepts(k, -PropHeightLimitsV1.MaxBelowRadiusMeters));
    }

    [Fact]
    public void Limits_SweepOfHeights_NoKindAcceptsBeyond()
    {
        var layout = new PropLayoutV1();
        var accepted = 0;

        // A band lying wholly beyond the limit (for example 400 to 500 m) would be missed by the two points checked above.
        for (var height = 0.25f; height <= 20000f; height += 0.25f)
        {
            var above = PropHeightLimitsV1.MaxAboveRadiusMeters + height;
            var below = -PropHeightLimitsV1.MaxBelowRadiusMeters - height;
            Assert.DoesNotContain(Kinds, k => layout.Accepts(k, above));
            Assert.DoesNotContain(Kinds, k => layout.Accepts(k, below));
            accepted++;
        }

        Assert.True(accepted > 70000);
    }

    [Fact]
    public void Limits_EveryRegisteredLayout_StaysInsideThem()
    {
        // A layout added for another generator profile (V2) with a wider band must stop here, because the client bounds where a prop can
        // lie with these limits and would otherwise skip regions that hold props.
        Assert.NotEmpty(PropLayouts.All);
        foreach (var layout in PropLayouts.All)
        {
            for (var height = 0.01f; height <= 20000f; height += 0.25f)
            {
                Assert.DoesNotContain(Kinds, k => layout.Accepts(k, PropHeightLimitsV1.MaxAboveRadiusMeters + height));
                Assert.DoesNotContain(Kinds, k => layout.Accepts(k, -PropHeightLimitsV1.MaxBelowRadiusMeters - height));
            }
        }
    }
}
