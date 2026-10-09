namespace Horyzont.Client.Tools;

/// <summary>What the aim ray points at.</summary>
public enum ToolTargetKind : byte
{
    /// <summary>Nothing within reach.</summary>
    None = 0,

    /// <summary>The terrain.</summary>
    Terrain = 1,

    /// <summary>A cell of a construct.</summary>
    ConstructCell = 2,
}
