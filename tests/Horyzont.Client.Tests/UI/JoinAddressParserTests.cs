using Horyzont.Client.UI;

namespace Horyzont.Client.Tests.UI;

public sealed class JoinAddressParserTests
{
    [Theory]
    [InlineData(" 127.0.0.1 ", "27015", "127.0.0.1", 27015)]
    [InlineData("example.org", "1", "example.org", 1)]
    [InlineData("::1", "65535", "::1", 65535)]
    [InlineData("2001:db8::1", "27015", "2001:db8::1", 27015)]
    public void Parse_Ipv4DnsIpv6AndValidPort_ReturnsHost(string address, string portText, string expected, int expectedPort)
    {
        Assert.True(JoinAddressParser.TryParse(address, portText, out string host, out int port));
        Assert.Equal(expected, host);
        Assert.Equal(expectedPort, port);
    }

    [Theory]
    [InlineData("localhost", "0")]
    [InlineData("localhost", "65536")]
    [InlineData("localhost", "+1")]
    [InlineData("localhost", "1.0")]
    [InlineData("http://localhost", "1")]
    [InlineData("user@localhost", "1")]
    [InlineData("localhost/path", "1")]
    [InlineData("[::1]", "1")]
    [InlineData("a b", "1")]
    [InlineData("-invalid.org", "1")]
    [InlineData("", "1")]
    [InlineData("localhost:27015", "1")]
    public void Parse_InvalidAddressOrPort_RejectsBeforeConnect(string address, string portText)
    {
        Assert.False(JoinAddressParser.TryParse(address, portText, out _, out _));
    }

    [Fact]
    public void Parse_LengthAndUtf8Password_EnforcesByteLimits()
    {
        Assert.False(JoinAddressParser.TryParse(new string('a', 254), "1", out _, out _));
        Assert.True(JoinAddressParser.IsPasswordValid(new string('a', 64)));
        Assert.False(JoinAddressParser.IsPasswordValid(new string('a', 65)));
        Assert.True(JoinAddressParser.IsPasswordValid(new string('ą', 32)));
        Assert.False(JoinAddressParser.IsPasswordValid(new string('ą', 33)));
    }
}
