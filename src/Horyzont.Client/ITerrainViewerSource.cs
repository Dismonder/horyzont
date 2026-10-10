namespace Horyzont.Client;

/// <summary>Reads terrain interests on the client main thread.</summary>
public interface ITerrainViewerSource
{
    /// <summary>Reads viewers when a player has an active planet.</summary>
    /// <param name="viewers">Current viewers.</param>
    /// <returns>True when at least one viewer exists.</returns>
    bool TryGetViewers(out TerrainViewers viewers);
}
