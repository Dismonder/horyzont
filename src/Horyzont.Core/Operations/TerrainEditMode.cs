namespace Horyzont.Core.Operations;

/// <summary>Terrain edit mode.</summary>
public enum TerrainEditMode : byte
{
    /// <summary>Digs (material 0).</summary>
    Remove = 1,

    /// <summary>Adds material.</summary>
    Add = 2,
}
