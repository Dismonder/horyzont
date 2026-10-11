using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Core.Space;

/// <summary>A body of a <see cref="StarSystemModel"/>: its placement joined with its planet parameters.</summary>
/// <param name="Planet">The planet.</param>
/// <param name="BodyIndex">Index of the body in the system.</param>
/// <param name="Position">Centre in the system frame.</param>
/// <param name="RadiusMeters">Surface radius R.</param>
/// <param name="SurfaceGravity">Surface gravity g₀ in m/s².</param>
public readonly record struct BodyInfo(PlanetId Planet, byte BodyIndex, Vector3D Position, double RadiusMeters, double SurfaceGravity);
