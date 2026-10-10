namespace Horyzont.Bots.Scripting;

/// <summary>Phase of a bot schedule.</summary>
public enum BotPhase
{
    /// <summary>Regular behaviour of the role.</summary>
    Normal = 0,

    /// <summary>Builders edit up to the rate limits for a short time.</summary>
    Burst = 1,

    /// <summary>No intents are sent, so the state settles before a checkpoint comparison.</summary>
    Quiet = 2,

    /// <summary>No intents and no movement while the reliable channels drain; the dump is taken at the end of it.</summary>
    Drain = 3,
}
