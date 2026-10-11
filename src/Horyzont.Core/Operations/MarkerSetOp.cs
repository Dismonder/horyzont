using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Markers;

namespace Horyzont.Core.Operations;

/// <summary>
/// Creates a map marker or replaces an existing one (K5-27). The owner of an existing marker never changes. A new id must be at least
/// <c>next_marker_id</c>; the applier moves the counter.
/// </summary>
/// <param name="Marker">Marker id.</param>
/// <param name="Owner">Owner of a private marker; null for a shared one.</param>
/// <param name="Planet">Planet whose frame holds the position.</param>
/// <param name="Position">Position in the planet frame.</param>
/// <param name="Name">Name, 1..32 UTF-8 bytes.</param>
/// <param name="Icon">Icon.</param>
/// <param name="Color">Colour.</param>
public sealed record MarkerSetOp(MarkerId Marker, PlayerId? Owner, PlanetId Planet, Vector3 Position, string Name, MarkerIcon Icon, MarkerColor Color)
    : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.MarkerSet;

    /// <summary>The record this operation stores.</summary>
    /// <returns>The marker record.</returns>
    public MarkerRecord ToRecord() => new(Marker, Owner, Planet, Position, Name, Icon, Color);
}
