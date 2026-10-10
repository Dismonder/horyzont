using Godot;

using Horyzont.Core.Ids;
using Horyzont.Game.Terrain;

namespace Horyzont.Game.Client;

/// <summary>The read-only window of the client onto a <see cref="PlanetTerrain"/> (see <see cref="IClientTerrainView"/>).</summary>
internal sealed class ClientTerrainView : IClientTerrainView
{
    private readonly PlanetTerrain _node;

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
    public void RemoveViewer(VoxelViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (viewer.GetParent() == Anchor)
        {
            Anchor.RemoveChild(viewer);
        }
    }
}
