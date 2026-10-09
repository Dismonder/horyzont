namespace Horyzont.Game.Terrain;

/// <summary>
/// The one conversion between <see cref="System.Numerics.Vector3"/> (Core, float, planet frame) and <see cref="Godot.Vector3"/>
/// (double with <c>GodotFloat64</c>, voxel space). Planet terrain stands at the planet centre with a unit basis and 1 m voxels, so
/// both are the same coordinates; only the precision changes. Converting in one place keeps rounding at block borders identical.
/// </summary>
public static class TerrainVectors
{
    /// <summary>Core position to voxel space (exact: every float is a double).</summary>
    public static Godot.Vector3 ToGodot(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

    /// <summary>Voxel space position to Core (rounded to the nearest float).</summary>
    public static System.Numerics.Vector3 ToCore(Godot.Vector3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
}
