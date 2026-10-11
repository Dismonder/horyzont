using Godot;

using Horyzont.Core.Ids;

namespace Horyzont.Game.Planet.Impostors;

/// <summary>
/// The impostor of one body (K5-22 decision 12): a mesh in the planet frame under <c>Planet_&lt;id&gt;</c> of the client frame nodes,
/// without shadows or collisions.
/// </summary>
public sealed partial class PlanetImpostor : MeshInstance3D
{
    /// <summary>Gets the body.</summary>
    public PlanetId Planet { get; private set; }

    /// <summary>Gets the build time of the mesh in milliseconds.</summary>
    public double BuildMilliseconds { get; private set; }

    /// <summary>Creates the node for built arrays; main thread.</summary>
    /// <param name="planet">The body.</param>
    /// <param name="data">The mesh arrays.</param>
    /// <param name="material">The shared impostor material.</param>
    /// <returns>The node, not yet in the tree.</returns>
    public static PlanetImpostor Create(PlanetId planet, PlanetImpostorMeshData data, Material material)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new PlanetImpostor
        {
            Name = "Impostor",
            Planet = planet,
            BuildMilliseconds = data.BuildMilliseconds,
            Mesh = PlanetImpostorMeshBuilder.ToMesh(data),
            MaterialOverride = material,
            CastShadow = ShadowCastingSetting.Off,
            GIMode = GIModeEnum.Disabled,
        };
    }
}
