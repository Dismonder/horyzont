using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Core.Serialization;

/// <summary>Binary codec for protocol messages: message type, format version, payload.</summary>
public static class MessageCodec
{
    /// <summary>Appends the frame of <paramref name="message"/>; throws <see cref="ArgumentException"/> when it exceeds the channel limit.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="message">Message.</param>
    public static void Write(ByteWriter w, Message message) => throw new NotImplementedException();

    /// <summary>Decodes a whole frame; the channel limit is checked first and trailing bytes are an error.</summary>
    /// <param name="frame">Frame bytes.</param>
    /// <returns>The message.</returns>
    public static Message Read(ReadOnlySpan<byte> frame) => throw new NotImplementedException();

    /// <summary>Reads bytes 2-3 of a ClientHello frame whatever its FormatVersion.</summary>
    /// <param name="frame">Frame bytes.</param>
    /// <param name="version">The protocol version when the method returns true.</param>
    /// <returns>True when the frame is a ClientHello with a readable version.</returns>
    public static bool TryReadProtocolVersion(ReadOnlySpan<byte> frame, out ushort version) => throw new NotImplementedException();
}
