using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>First message of a client. The first fields (type, format version, protocol version) are frozen forever.</summary>
/// <param name="ProtocolVersion">Client protocol version.</param>
/// <param name="BuildHash">Hash of the client build.</param>
/// <param name="Identity">Platform identity.</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="Password">Server password; empty when none.</param>
/// <param name="AuthTicket">Platform ticket; may be empty.</param>
#pragma warning disable CA1819 // Message records own their arrays (decoders copy them).
public sealed record ClientHello(
    ushort ProtocolVersion, ulong BuildHash, PlatformIdentity Identity, string DisplayName, string Password, byte[] AuthTicket) : Message
#pragma warning restore CA1819
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ClientHello;
}
