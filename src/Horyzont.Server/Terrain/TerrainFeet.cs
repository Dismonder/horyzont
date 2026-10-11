namespace Horyzont.Server.Terrain;

/// <summary>Result of <see cref="ITerrainReplicationHost.GetFeet"/> (K5-22 decision 8).</summary>
public enum TerrainFeet : byte
{
    /// <summary>The player has no body (none spawned yet, or a server without characters): positions are not checked.</summary>
    NoBody = 0,

    /// <summary>The body is farther than <see cref="TerrainReplicationPolicy.OtherPlanetViewFactor"/> R from the planet centre.</summary>
    TooFar = 1,

    /// <summary>The feet are expressed in the planet frame.</summary>
    Found = 2,
}
