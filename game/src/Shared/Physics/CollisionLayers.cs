namespace Horyzont.Game.Physics;

/// <summary>
/// Names of the 3D physics layers (numbers 1 … 32 as in the Godot project settings) and their bit masks. The names are also set
/// in <c>project.godot</c> (<c>layer_names/3d_physics</c>).
/// </summary>
public static class CollisionLayers
{
    /// <summary>Layer number of the voxel terrain.</summary>
    public const int Terrain = 1;

    /// <summary>Layer number of construct chunk bodies.</summary>
    public const int Constructs = 2;

    /// <summary>Layer number of the authoritative character bodies on the server.</summary>
    public const int ServerBodies = 3;

    /// <summary>Layer number of the predicted character body on a client.</summary>
    public const int ClientBodies = 4;

    /// <summary>Bit mask of <see cref="Terrain"/>.</summary>
    public const uint TerrainMask = 1u << (Terrain - 1);

    /// <summary>Bit mask of <see cref="Constructs"/>.</summary>
    public const uint ConstructsMask = 1u << (Constructs - 1);

    /// <summary>Bit mask of <see cref="ServerBodies"/>.</summary>
    public const uint ServerBodiesMask = 1u << (ServerBodies - 1);

    /// <summary>Bit mask of <see cref="ClientBodies"/>.</summary>
    public const uint ClientBodiesMask = 1u << (ClientBodies - 1);
}
