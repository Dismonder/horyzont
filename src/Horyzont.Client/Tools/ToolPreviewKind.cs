namespace Horyzont.Client.Tools;

/// <summary>What the preview draws.</summary>
public enum ToolPreviewKind : byte
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The sphere of the terrain manipulator.</summary>
    Sphere = 1,

    /// <summary>The ghost of the first block of a new construct on the terrain.</summary>
    NewConstructCell = 2,

    /// <summary>The ghost of a block added to an existing construct.</summary>
    ConstructCell = 3,
}
