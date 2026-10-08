namespace Horyzont.Net.Transport;

/// <summary>Address of a transport peer; the concrete subtype depends on the transport.</summary>
public abstract record TransportEndpoint
{
    private protected TransportEndpoint()
    {
    }
}
