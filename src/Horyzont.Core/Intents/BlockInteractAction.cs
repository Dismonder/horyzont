namespace Horyzont.Core.Intents;

/// <summary>What a player does to a functional block with a <see cref="BlockInteractIntent"/> (spec 03).</summary>
public enum BlockInteractAction : byte
{
    /// <summary>Switch a door or a lamp.</summary>
    Toggle = 1,

    /// <summary>Sleep in a bed (sets the respawn point; resting arrives with K6-33).</summary>
    Sleep = 2,

    /// <summary>Set the respawn point at a bed without sleeping.</summary>
    SetRespawn = 3,
}
