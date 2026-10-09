namespace Horyzont.Game.Planet;

/// <summary>
/// Terrain materials known to the game: material key and the texture index stored in the voxels (channel
/// <see cref="TerrainChannels.Material"/>). Append only: the indices are stored in saved terrain blocks and in generator
/// output (frozen by ZASADY point 3).
/// </summary>
public static class TerrainMaterials
{
    public const string StoneKey = "core:stone";
    public const string DirtKey = "core:dirt";
    public const string SandKey = "core:sand";
    public const string OreKey = "core:ore";

    /// <summary>Material of the indestructible core (excluded from the build palette).</summary>
    public const string CoreMaterialKey = "core:bedrock";

    public const byte StoneTextureIndex = 0;
    public const byte DirtTextureIndex = 1;
    public const byte SandTextureIndex = 2;
    public const byte OreTextureIndex = 3;
    public const byte CoreTextureIndex = 4;

    /// <summary>Materials in registration order for a new world.</summary>
    public static IReadOnlyList<(string Key, byte TextureIndex)> All { get; } =
    [
        (StoneKey, StoneTextureIndex),
        (DirtKey, DirtTextureIndex),
        (SandKey, SandTextureIndex),
        (OreKey, OreTextureIndex),
        (CoreMaterialKey, CoreTextureIndex),
    ];

    /// <summary>Keys in registration order, for <c>WorldState.CreateNew</c>.</summary>
    public static IReadOnlyList<string> Keys { get; } = All.Select(m => m.Key).ToArray();
}
