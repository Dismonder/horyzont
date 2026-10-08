namespace Horyzont.Core.Serialization;

/// <summary>
/// Thrown by every decoder when external data (network frame, journal entry, SQLite blob) is truncated, malformed, out of range or over a limit.
/// It is the only exception a decoder may throw for bad input.
/// </summary>
/// <param name="reason">Human-readable description of the violation.</param>
public sealed class ProtocolViolationException(string reason) : Exception(reason);
