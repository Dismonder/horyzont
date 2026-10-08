using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Net.Session;

/// <summary>An entity that players may see.</summary>
/// <param name="Entity">The entity.</param>
/// <param name="Region">
/// Frame in which distances are measured: the planet frame, and from subproject 2 also the system frame.
/// </param>
/// <param name="Position">Position in the region's frame; for entities in a construct frame the game composes the frames.</param>
/// <param name="Radius">Bounding radius of the subject, in metres.</param>
/// <param name="ViewRange">Distance from which the subject is visible, in metres.</param>
public readonly record struct InterestSubject(EntityId Entity, FrameRef Region, Vector3 Position, float Radius, float ViewRange);
