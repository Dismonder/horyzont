namespace Horyzont.Core.Serialization;

/// <summary>First byte of every SQLite blob written by <see cref="StateCodec"/>.</summary>
public enum BlobKind : byte
{
    /// <summary>A construct header record.</summary>
    ConstructHeader = 1,

    /// <summary>A player record.</summary>
    Player = 2,

    /// <summary>The block array of one construct chunk.</summary>
    ConstructChunk = 3,

    /// <summary>Planet parameters.</summary>
    PlanetParams = 4,

    /// <summary>The voxels of one LOD 0 terrain block (K2-09); the payload format is opaque to Core.</summary>
    TerrainBlock = 5,

    /// <summary>The star system of a world (K5-21): placement of the bodies and the sun cycle.</summary>
    StarSystemParams = 10,

    /// <summary>A map marker (K5-27).</summary>
    Marker = 11,

    /// <summary>The flight state of a ship (K5-24): fuel, velocities and flags.</summary>
    ShipState = 12,
    /// <summary>The settings of a world (K6-35); the content of operation 46. Absent in a world that never left the defaults.</summary>
    WorldSettings = 23,

    /// <summary>A persistent entity (K6-35): a tombstone or an item lying in the world; the content of operation 38.</summary>
    PersistentEntity = 24,

    /// <summary>A container record (K6-29).</summary>
    Container = 20,

    /// <summary>The state of a functional block (K6-30).</summary>
    BlockEntity = 21,

    /// <summary>The progress of a player; in version 1 only the respawn point (K6-30), in version 2 also the discovered item types (K6-32).</summary>
    PlayerProgress = 22,

    /// <summary>The progress keys of a world (K6-32).</summary>
    ProgressKeys = 25,
}
