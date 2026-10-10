namespace Horyzont.Bots.Scripting;

/// <summary>What a bot does with its tools in one tick.</summary>
public enum BotAction
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Remove terrain at the aim point.</summary>
    Dig = 1,

    /// <summary>Add terrain at the aim point.</summary>
    Fill = 2,

    /// <summary>Place a block against the aimed surface.</summary>
    PlaceBlock = 3,

    /// <summary>Remove the aimed block.</summary>
    RemoveBlock = 4,

    /// <summary>Abuser: dig at a point far beyond the reach.</summary>
    AbuseOutOfReach = 5,

    /// <summary>Abuser: a dig with a radius far above the maximum.</summary>
    AbuseBadRadius = 6,

    /// <summary>Abuser: an intent whose direction is not finite (the client must refuse to send it).</summary>
    AbuseNotFinite = 7,

    /// <summary>Abuser: a terrain edit in every tick of a series, much faster than the allowed rate.</summary>
    AbuseTooFast = 8,
}
