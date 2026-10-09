namespace Horyzont.Game.Planet;

/// <summary>Which parts of a planet terrain node are active.</summary>
/// <param name="Collisions">Generate collision shapes (LOD 0 only).</param>
/// <param name="Render">Create meshes with the terrain material and shadows.</param>
public readonly record struct PlanetTerrainOptions(bool Collisions, bool Render)
{
    /// <summary>Dedicated server: collisions, no rendering.</summary>
    public static PlanetTerrainOptions Server => new(Collisions: true, Render: false);

    /// <summary>Client, also the host (one instance for both).</summary>
    public static PlanetTerrainOptions Client => new(Collisions: true, Render: true);

    /// <summary>Generator preview (<c>--planet-preview</c>): rendering without collisions.</summary>
    public static PlanetTerrainOptions Preview => new(Collisions: false, Render: true);
}
