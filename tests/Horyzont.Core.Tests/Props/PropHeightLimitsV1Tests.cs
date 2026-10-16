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
}
