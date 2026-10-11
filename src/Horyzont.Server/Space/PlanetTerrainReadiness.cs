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
    /// The node is active and the LOD 0 collision is built: for a sphere of at most <see cref="TerrainSweep.FullBoxMaxRadius"/> (the
    /// sweep box of a ship) in every LOD 0 block intersecting its box; for a larger sphere (a column) in the ground box of
    /// <see cref="TerrainGround"/> when the column reaches the generated ground. A sphere entirely above <c>R + MaxRelief + 64 m</c> is
    /// ready as soon as the node is active. A ship is ready when its column and, in the band, its sweep box are
    /// (<see cref="PlanetTerrainReadinessQueries.GetShipReadiness"/>).
    /// </summary>
    Ready = 2,

    /// <summary>
    /// The terrain stream of the node reported a fault or the block store is saturated: the area will not load. Reported at once;
    /// <see cref="Streaming"/> never masks a fault.
    /// </summary>
    Faulted = 3,
}
