namespace Horyzont.Server;

/// <summary>
/// Phase of a server tick participant (K5-20 decision 8): frames and constructs move first, then the characters in those frames, then
/// systems that read the composed poses. Within a phase participants run in registration order.
/// </summary>
public enum ServerTickPhase : byte
{
    /// <summary>Reference frames and constructs (ship flight K5-24, zone transitions K5-21).</summary>
    Frames = 0,

    /// <summary>Characters in their frames (the default: every participant before K5-20).</summary>
    Characters = 1,

    /// <summary>Readers of composed poses: <c>ServerFrameHistory</c> first, then terrain viewers and status senders.</summary>
    Compose = 2,
}
