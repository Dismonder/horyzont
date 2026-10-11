using Godot;

using Horyzont.Core.Ids;

namespace Horyzont.Game.Client;

/// <summary>
/// What client code may do with the terrain of one planet: place viewers and visuals under it and read its collision layers.
/// It deliberately offers no <c>VoxelLodTerrain</c>, <c>VoxelTool</c> or <c>PlanetTerrain</c>: in solo and host mode the terrain is
/// the authoritative one of the server, and every change of it must go through <c>ServerSimulation</c> (operation, journal, versions).
/// </summary>
public interface IClientTerrainView
{
    /// <summary>Gets the planet of the terrain.</summary>
    PlanetId Planet { get; }

    /// <summary>Gets the node at the planet centre (planet frame, metres) to parent client visuals under.</summary>
    Node3D Anchor { get; }

    /// <summary>Gets the physics layer of the terrain collisions.</summary>
    uint CollisionLayer { get; }

    /// <summary>Adds a viewer that streams (and, when asked, renders or collides) the terrain around it; main thread.</summary>
    /// <param name="viewer">The viewer; its position is in the planet frame.</param>
    void AddViewer(VoxelViewer viewer);

    /// <summary>
    /// Sets the share of the impostor in the cross-fade (K5-22 decision 13): the terrain discards the dither pixels the impostor draws.
    /// The weight is quantized to the dither levels; the render material of the planet changes only when the level does. Main thread.
    /// </summary>
    /// <param name="impostorWeight">The impostor share in [0, 1]; 0 = terrain only.</param>
    void SetImpostorBlend(float impostorWeight);

    /// <summary>Removes a viewer added with <see cref="AddViewer"/>; main thread.</summary>
    /// <param name="viewer">The viewer.</param>
    void RemoveViewer(VoxelViewer viewer);
}
