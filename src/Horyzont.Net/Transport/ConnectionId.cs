namespace Horyzont.Net.Transport;

/// <summary>Identifier of one connection, assigned by the transport instance and never reused by it.</summary>
/// <param name="Value">Raw identifier value; zero is never assigned.</param>
public readonly record struct ConnectionId(ulong Value);
