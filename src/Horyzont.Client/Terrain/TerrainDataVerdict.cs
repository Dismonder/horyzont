namespace Horyzont.Client.Terrain;

/// <summary>What the client does with a block the server sent.</summary>
public enum TerrainDataVerdict
{
    /// <summary>The block is authoritative: store it and show it.</summary>
    Accept = 0,

    /// <summary>An older version than one already taken in this session (or than the manifest asks for): ignore it.</summary>
    Stale = 1,

    /// <summary>The block contradicts the manifest of the server: the peer is faulty or hostile.</summary>
    Violation = 2,
}
