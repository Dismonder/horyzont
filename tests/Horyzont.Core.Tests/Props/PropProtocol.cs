using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;

namespace Horyzont.Core.Tests.Props;

/// <summary>
/// Codes of K6-31 that have no enum member yet: they arrive with the protocol in step B2 (K6-31 D1), so the tests name them by number.
/// </summary>
internal static class PropProtocol
{
    /// <summary>Reject reason 53: the tool is too weak for the prop or the terrain material.</summary>
    public const RejectReason ToolTierTooLow = (RejectReason)53;

    /// <summary>Message 45, <c>PropStates</c>.</summary>
    public const MessageType PropStatesType = (MessageType)45;
}
