namespace Horyzont.Client.Movement;

/// <summary>Result of comparing a server state with the prediction.</summary>
public enum AckOutcome : byte
{
    /// <summary>The acknowledgement is not newer than a previous one and was ignored.</summary>
    Stale = 0,

    /// <summary>The prediction agrees with the server; acknowledged entries were dropped.</summary>
    InSync = 1,

    /// <summary>The prediction diverged or has no entry: set the body to the server state and replay the unacknowledged inputs.</summary>
    Correct = 2,
}
