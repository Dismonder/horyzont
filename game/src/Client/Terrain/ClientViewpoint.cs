using Horyzont.Core.Ids;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Terrain;

/// <summary>
/// Where the own body and the camera are, as <see cref="ClientPlanetTerrains"/> reads them every frame (K5-22 decision 10).
/// </summary>
/// <param name="Forced">The region planet of the own body (forced active); default when the body is not in a planet region.</param>
/// <param name="Planet">The planet frame of <paramref name="Camera"/> and <paramref name="Feet"/>; default without a body.</param>
/// <param name="PlanetRadius">The radius of <paramref name="Planet"/> in metres.</param>
/// <param name="Camera">The camera origin in the frame of <paramref name="Planet"/>.</param>
/// <param name="Feet">The feet of the own body in the frame of <paramref name="Planet"/>.</param>
public readonly record struct ClientViewpoint(PlanetId Forced, PlanetId Planet, float PlanetRadius, NumVector3 Camera, NumVector3 Feet)
{
    /// <summary>Gets a value indicating whether there is an own body.</summary>
    public bool HasBody => Planet != default;
}
