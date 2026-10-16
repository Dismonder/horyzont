namespace Horyzont.Client.Props;

/// <summary>What the press of the primary button would do to the prop under the crosshair.</summary>
public enum PropPromptKind : byte
{
    /// <summary>Fell a tree.</summary>
    CutTree = 0,

    /// <summary>Cut a bush.</summary>
    CutBush = 1,

    /// <summary>Break a rock or a boulder.</summary>
    BreakRock = 2,

    /// <summary>Mine crystals.</summary>
    MineCrystals = 3,

    /// <summary>The tool in hand is too weak for the prop; nothing happens.</summary>
    ToolTierTooLow = 4,
}
