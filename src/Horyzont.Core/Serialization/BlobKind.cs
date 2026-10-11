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
}
