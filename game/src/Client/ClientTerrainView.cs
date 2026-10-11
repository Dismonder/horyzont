using Godot;

using Horyzont.Client.Space;
using Horyzont.Core.Ids;
using Horyzont.Game.Terrain;

namespace Horyzont.Game.Client;

/// <summary>The read-only window of the client onto a <see cref="PlanetTerrain"/> (see <see cref="IClientTerrainView"/>).</summary>
internal sealed class ClientTerrainView : IClientTerrainView
{
    private readonly PlanetTerrain _node;
    private readonly ShaderMaterial?[] _blendMaterials = new ShaderMaterial?[ImpostorBlend.Levels + 1];
    private ShaderMaterial? _baseMaterial;
    private int _blendLevel;

    /// <summary>Creates the view with its own anchor node under the terrain node.</summary>
    /// <param name="node">The terrain node of the planet.</param>
    internal ClientTerrainView(PlanetTerrain node)
    {
        _node = node;
        Anchor = new Node3D { Name = "ClientAnchor" };
        node.AddChild(Anchor);
    }

    /// <inheritdoc />
    public PlanetId Planet => _node.Planet;

    /// <inheritdoc />
    public Node3D Anchor { get; }

    /// <inheritdoc />
    public uint CollisionLayer => (uint)_node.Terrain.CollisionLayer;

    /// <summary>Gets a value indicating whether the terrain node still lives.</summary>
    internal bool IsValid => GodotObject.IsInstanceValid(_node) && GodotObject.IsInstanceValid(Anchor);

    /// <inheritdoc />
    public void AddViewer(VoxelViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        Anchor.AddChild(viewer);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Voxel Tools copies the parameters of the terrain material into a material of every mesh block when the block is made, so a
    /// parameter set later never reaches existing blocks; <c>set_material</c> with another material does (it reallocates the block
    /// materials). Each of the 17 levels gets its own copy of the material of the planet, made on first use and kept.
    /// </remarks>
    public void SetImpostorBlend(float impostorWeight)
    {
        var level = (int)MathF.Round(ImpostorBlend.Quantize(impostorWeight) * ImpostorBlend.Levels);
        if (level == _blendLevel || !GodotObject.IsInstanceValid(_node))
        {
            return;
        }

        if (_baseMaterial is null)
        {
            if (_node.Terrain.GetMaterial() is not ShaderMaterial current)
            {
                return;
            }

            _baseMaterial = current;
            _blendMaterials[0] = current;
        }

        var material = _blendMaterials[level];
        if (material is null)
        {
            material = (ShaderMaterial)_baseMaterial.Duplicate();
            material.SetShaderParameter("u_blend", (float)level / ImpostorBlend.Levels);
            _blendMaterials[level] = material;
        }

        _node.Terrain.SetMaterial(material);
        _blendLevel = level;
    }

    /// <inheritdoc />
    public void RemoveViewer(VoxelViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (viewer.GetParent() == Anchor)
        {
            Anchor.RemoveChild(viewer);
        }
    }
}
