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

    private ShaderMaterial? _material;
    private float _blend = 1f;

    /// <summary>Gets or sets the share of the impostor in the cross-fade (decision 13); hidden at 0.</summary>
    public float Blend
    {
        get => _blend;
        set
        {
            if (value == _blend)
            {
                return;
            }

            _blend = value;
            _material?.SetShaderParameter("u_blend", value);
            Visible = value > 0f;
        }
    }

    /// <summary>Gets the build time of the mesh in milliseconds.</summary>
    public double BuildMilliseconds { get; private set; }

    /// <summary>Creates the node for built arrays; main thread.</summary>
    /// <param name="planet">The body.</param>
    /// <param name="data">The mesh arrays.</param>
    /// <param name="material">The impostor material (copied for the body: the blend is per body).</param>
    /// <returns>The node, not yet in the tree.</returns>
    public static PlanetImpostor Create(PlanetId planet, PlanetImpostorMeshData data, ShaderMaterial material)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(material);
        var own = (ShaderMaterial)material.Duplicate();
        return new PlanetImpostor
        {
            _material = own,
            Name = "Impostor",
            Planet = planet,
            BuildMilliseconds = data.BuildMilliseconds,
            Mesh = PlanetImpostorMeshBuilder.ToMesh(data),
            MaterialOverride = own,
            CastShadow = ShadowCastingSetting.Off,
            GIMode = GIModeEnum.Disabled,
        };
    }
}
