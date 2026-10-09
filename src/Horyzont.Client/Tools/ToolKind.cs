namespace Horyzont.Client.Tools;

/// <summary>The tool the local player holds.</summary>
public enum ToolKind : byte
{
    /// <summary>Digs and fills the terrain with a sphere.</summary>
    TerrainManipulator = 1,

    /// <summary>Places, turns and removes blocks of constructs.</summary>
    Builder = 2,
}
