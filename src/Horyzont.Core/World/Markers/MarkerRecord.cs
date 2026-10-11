using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Markers;

/// <summary>
/// A map marker (K5-27 decision 1): always in the frame of a planet or moon. Saved in the <c>markers</c> table (blob 11), hashed,
/// and sent in <c>MarkersState</c>.
/// </summary>
/// <param name="Id">Marker id.</param>
/// <param name="Owner">Owning player for a private marker; null for a shared one. Immutable.</param>
/// <param name="Planet">Planet (or moon) whose frame holds the position.</param>
/// <param name="Position">Position in the planet frame, metres.</param>
/// <param name="Name">Name, 1..32 UTF-8 bytes.</param>
/// <param name="Icon">Icon.</param>
/// <param name="Color">Colour.</param>
public sealed record MarkerRecord(MarkerId Id, PlayerId? Owner, PlanetId Planet, Vector3 Position, string Name, MarkerIcon Icon, MarkerColor Color)
{
    /// <summary>True for a shared marker.</summary>
    public bool IsShared => Owner is null;
}
