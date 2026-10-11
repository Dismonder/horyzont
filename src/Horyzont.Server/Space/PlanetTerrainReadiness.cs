namespace Horyzont.Server.Space;

/// <summary>
/// Readiness of the terrain of a planet in a query sphere (K5-22 decision 6, README Y11 — the conservative definition of the K5
/// review, B3). Flight (K5-24) maps it to its terrain gate; the timeout and the <c>Unavailable</c> path belong to the consumer.
/// </summary>
public enum PlanetTerrainReadiness : byte
{
    /// <summary>The planet has no terrain node; the query itself never activates it (demand does — K5-22 decision 2).</summary>
    Inactive = 0,

    /// <summary>The node exists, but the voxel data or the LOD 0 collision in the sphere is not built yet.</summary>
    Streaming = 1,

    /// <summary>
    /// The node is active, the voxel data in the sphere is loaded and the collision of every LOD 0 block intersecting the sphere is
    /// built; a sphere entirely above <c>R + MaxRelief + 64 m</c> is ready as soon as the node is active.
    /// </summary>
    Ready = 2,

    /// <summary>
    /// The terrain stream of the node reported a fault or the block store is saturated: the area will not load. Reported at once;
    /// <see cref="Streaming"/> never masks a fault.
    /// </summary>
    Faulted = 3,
}
