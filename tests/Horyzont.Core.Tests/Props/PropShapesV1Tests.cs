using Horyzont.Core.Props;

namespace Horyzont.Core.Tests.Props;

/// <summary>
/// The stand-in bodies of layout V1 (K6-31 DO USTALENIA-13): the validator and the client picker must measure alike, so the values are pinned.
/// </summary>
public sealed class PropShapesV1Tests
{
    public static TheoryData<PropKindV1, float, float> Golden() => new()
    {
        { PropKindV1.Pine, 9f, 0.35f },
        { PropKindV1.PineTall, 14f, 0.45f },
        { PropKindV1.Broadleaf, 8f, 0.4f },
        { PropKindV1.BroadleafAutumn, 8f, 0.4f },
        { PropKindV1.Bush, 1.2f, 0.5f },
        { PropKindV1.Rock, 0.6f, 0.5f },
        { PropKindV1.Boulder, 1.4f, 1.2f },
        { PropKindV1.Crystals, 1.0f, 0.6f },
    };

    [Theory]
    [MemberData(nameof(Golden))]
    public void Shapes_GoldenValues(PropKindV1 kind, float height, float radius)
    {
        var shape = PropShapesV1.Get(kind);

        Assert.Equal(height, shape.HeightMeters);
        Assert.Equal(radius, shape.RadiusMeters);
    }

    [Fact]
    public void Shapes_EveryKindHasOne()
    {
        var kinds = Enum.GetValues<PropKindV1>().Where(k => k != PropKindV1.None).ToArray();

        Assert.Equal(Golden().Count, kinds.Length);
        Assert.All(kinds, k =>
        {
            Assert.True(PropShapesV1.Get(k).HeightMeters > 0f, $"{k} has no height.");
            Assert.True(PropShapesV1.Get(k).RadiusMeters > 0f, $"{k} has no radius.");
        });
    }
}
