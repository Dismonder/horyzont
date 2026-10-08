using Horyzont.Net.Transport.Udp;

namespace Horyzont.Net.Tests.Transport;

public sealed class UdpTransportTests : TransportContractTests<UdpWorld>
{
    [Fact]
    public void MaxUnreliablePayload_Default_IsAtLeast1150()
    {
        using var transport = new UdpTransport(World.Options);

        Assert.True(transport.MaxUnreliablePayload >= 1150);
    }
}
