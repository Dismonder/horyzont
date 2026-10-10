namespace Horyzont.Bots.Scripting;

/// <summary>The behaviour a bot follows (K4-19, P6).</summary>
public enum BotRole
{
    /// <summary>Stays in the shared zone and edits terrain and blocks at a steady pace, with a burst now and then.</summary>
    Builder = 0,

    /// <summary>Walks out to the edge of the world around the zone and back, entering and leaving the interest range.</summary>
    Explorer = 1,

    /// <summary>Sends intents that the server must refuse: out of reach, too fast, under the core, not finite.</summary>
    Abuser = 2,
}
