using System.Numerics;
using Horyzont.Core.Ids;
namespace Horyzont.Core.Protocol.Messages;
/// <summary>Remote client's visual and collision terrain viewers.</summary>
/// <param name="Planet">Viewer planet.</param>
/// <param name="ViewSeq">Monotonic session sequence.</param>
/// <param name="VisualPosition">Visual viewer in the planet frame.</param>
/// <param name="VisualDistance">Visual distance in metres, at most 16384.</param>
/// <param name="CollisionPosition">Collision viewer in the planet frame.</param>
/// <param name="CollisionDistance">Collision distance in metres, at most 16384.</param>
public sealed record TerrainView(PlanetId Planet, uint ViewSeq, Vector3 VisualPosition, float VisualDistance,
    Vector3 CollisionPosition, float CollisionDistance) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.TerrainView;
}
