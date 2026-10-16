using Horyzont.Core.Operations;

namespace Horyzont.Core.Intents;

/// <summary>Result of submitting an intent.</summary>
/// <param name="Reason">Reject reason; <see cref="RejectReason.None"/> when accepted.</param>
/// <param name="Applied">The applied operation when accepted.</param>
public readonly record struct IntentResult(RejectReason Reason, AppliedOperation? Applied)
{
    /// <summary>True when the intent was accepted.</summary>
    public bool IsAccepted => Reason == RejectReason.None;

    /// <summary>An accepted result.</summary>
    /// <param name="applied">The applied operation.</param>
    /// <returns>The result.</returns>
    public static IntentResult Accepted(AppliedOperation applied) => new(RejectReason.None, applied);

    /// <summary>An accepted result that changed no world state and so has no operation: a hit that did not finish a prop (K6-31 D10).</summary>
    /// <returns>The result.</returns>
    public static IntentResult AcceptedWithoutOperation() => new(RejectReason.None, null);

    /// <summary>A rejected result.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The result.</returns>
    public static IntentResult Rejected(RejectReason reason) => new(reason, null);
}
