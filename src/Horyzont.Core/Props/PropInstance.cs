using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Props;

/// <summary>A prop of a planet with its place in the planet frame (K6-31 D7).</summary>
/// <param name="Id">The prop.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Anchor">Foot of the prop on the generated surface, in the frame of the planet.</param>
/// <param name="Up">Unit vector away from the planet centre at the anchor.</param>
/// <param name="Scale">Scale of the layout (0.4-2.2).</param>
public readonly record struct PropInstance(PropId Id, PropKindV1 Kind, Vector3 Anchor, Vector3 Up, float Scale)
{
    /// <summary>Length of the trunk segment from the anchor along <see cref="Up"/>, metres: <see cref="PropShapesV1"/> times <see cref="Scale"/>.</summary>
    public float TrunkHeightMeters => PropShapesV1.Get(Kind).HeightMeters * Scale;

    /// <summary>Radius of the stand-in capsule added to the reach, metres: <see cref="PropShapesV1"/> times <see cref="Scale"/>.</summary>
    public float BodyRadiusMeters => PropShapesV1.Get(Kind).RadiusMeters * Scale;
}
