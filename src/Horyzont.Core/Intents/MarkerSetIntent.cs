using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Markers;

namespace Horyzont.Core.Intents;

/// <summary>Create (<paramref name="Marker"/> is none) or edit a map marker (K5-27).</summary>
/// <param name="Marker">Marker to edit; <see cref="MarkerId.None"/> creates a new one.</param>
/// <param name="Shared">True for a shared marker; must match the existing marker on edit.</param>
/// <param name="Planet">Planet whose frame holds the position.</param>
/// <param name="Position">Position in the planet frame.</param>
/// <param name="Name">Name, 1..32 UTF-8 bytes.</param>
/// <param name="Icon">Icon.</param>
/// <param name="Color">Colour.</param>
public sealed record MarkerSetIntent(MarkerId Marker, bool Shared, PlanetId Planet, Vector3 Position, string Name, MarkerIcon Icon, MarkerColor Color)
    : Intent;
