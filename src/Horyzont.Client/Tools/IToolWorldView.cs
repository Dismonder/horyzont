using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Tools;

/// <summary>
/// Read-only view of the world for aiming and previews, in the planet frame. In solo and host mode it reads the authoritative
/// terrain and constructs; a remote client gets its own implementation (K3-15, K3-16). Calls happen every physics tick, so an
/// implementation must not allocate.
/// </summary>
public interface IToolWorldView
{
    /// <summary>Parameters of a planet.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="parameters">The parameters when the planet is known.</param>
    /// <returns>True when known.</returns>
    bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetParams? parameters);

    /// <summary>First terrain hit of a ray.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Unit direction.</param>
    /// <param name="maxDistance">Maximum distance.</param>
    /// <param name="hit">The hit.</param>
    /// <returns>True when the ray hits terrain within the distance.</returns>
    bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit);

    /// <summary>Nearest construct cell hit by a ray.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Unit direction.</param>
    /// <param name="maxDistance">Maximum distance.</param>
    /// <param name="hit">The hit.</param>
    /// <returns>True when a cell was hit within the distance.</returns>
    bool RaycastConstructs(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out ConstructHit hit);

    /// <summary>A construct, read only.</summary>
    /// <param name="construct">Construct id.</param>
    /// <param name="value">The construct when known.</param>
    /// <returns>True when known.</returns>
    bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value);

    /// <summary>Pose of a construct in the frame of its planet (composed along its parent chain).</summary>
    /// <param name="construct">Construct id.</param>
    /// <param name="origin">Corner of cell (0,0,0).</param>
    /// <param name="rotation">Rotation from construct space.</param>
    /// <returns>True when the construct is known and lies on a planet.</returns>
    bool TryGetConstructPose(EntityId construct, out Vector3 origin, out Quaternion rotation);

    /// <summary>The <see cref="BlockOverlap"/> rule against the constructs of a planet.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="center">Centre of the new cell.</param>
    /// <param name="rotation">Rotation of the new cell.</param>
    /// <param name="skip">Construct to leave out.</param>
    /// <returns>True when the new cell would overlap an existing block.</returns>
    bool OverlapsConstruct(PlanetId planet, Vector3 center, Quaternion rotation, EntityId? skip);

    /// <summary>Feet of other online players on the planet, for preview hints only (may be empty).</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="destination">Receives the feet; extra players are dropped.</param>
    /// <returns>Number of feet written.</returns>
    int CopyOtherPlayerFeet(PlanetId planet, Span<Vector3> destination);
}
