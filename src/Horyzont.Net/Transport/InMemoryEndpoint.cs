namespace Horyzont.Net.Transport;

/// <summary>Address of an in-memory transport: its name inside an in-memory network.</summary>
/// <param name="Name">Transport name.</param>
public sealed record InMemoryEndpoint(string Name) : TransportEndpoint;
