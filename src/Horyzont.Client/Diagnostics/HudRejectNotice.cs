using Horyzont.Core.Intents;

namespace Horyzont.Client.Diagnostics;

/// <summary>Decides when a refusal needs the note that the server applies its own limits; the limit itself is never guessed.</summary>
public static class HudRejectNotice
{
    /// <summary>Tells whether the refusal comes from a server limit (rate, reach) that the client can only approximate.</summary>
    /// <param name="reason">The refusal.</param>
    /// <returns>True for rate and reach refusals.</returns>
    public static bool IsServerLimit(RejectReason reason) => reason is RejectReason.RateLimited or RejectReason.OutOfReach;
}
