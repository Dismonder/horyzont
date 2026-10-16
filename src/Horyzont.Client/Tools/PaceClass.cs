namespace Horyzont.Client.Tools;

/// <summary>Rate class of a tool action, as on the server.</summary>
public enum PaceClass : byte
{
    /// <summary>Terrain edits: token bucket plus sliding window.</summary>
    Terrain = 0,

    /// <summary>Block edits: token bucket.</summary>
    Block = 1,

    /// <summary>Hits on props: token bucket.</summary>
    Prop = 2,
}
