namespace Horyzont.Net.Transport;

/// <summary>Address of a UDP transport.</summary>
/// <param name="Host">IP address literal to bind to (server) or connect to (client).</param>
/// <param name="Port">UDP port; zero lets the server pick a free port.</param>
public sealed record UdpEndpoint(string Host, int Port) : TransportEndpoint;
