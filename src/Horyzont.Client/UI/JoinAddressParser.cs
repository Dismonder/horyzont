using System.Globalization;
using System.Net;
using System.Text;

using Horyzont.Core.Protocol;

namespace Horyzont.Client.UI;

/// <summary>Validates separate host, port and password fields before allocating transport resources.</summary>
public static class JoinAddressParser
{
    /// <summary>Accepts a DNS name, IPv4 or unbracketed IPv6 and a decimal port from 1 to 65535.</summary>
    public static bool TryParse(string address, string portText, out string host, out int port)
    {
        host = "";
        port = 0;
        if (address is null || portText is null)
        {
            return false;
        }
        string candidate = address.Trim();
        if (candidate.Length is 0 or > 253 || candidate.Any(char.IsWhiteSpace)
            || candidate.IndexOfAny(['/', '\\', '@', '[', ']', '?', '#']) >= 0
            || !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            || parsed is < 1 or > 65535)
        {
            return false;
        }
        if (!IPAddress.TryParse(candidate, out _) && !IsDns(candidate))
        {
            return false;
        }
        host = candidate;
        port = parsed;
        return true;
    }

    /// <summary>Checks the protocol's UTF-8 password limit without storing the password.</summary>
    public static bool IsPasswordValid(string password) => password is not null
        && Encoding.UTF8.GetByteCount(password) <= ProtocolConstants.MaxPasswordBytes;

    private static bool IsDns(string host)
    {
        var labels = host.AsSpan();
        if (labels[^1] == '.')
        {
            labels = labels[..^1];
        }
        foreach (var range in labels.Split('.'))
        {
            var label = labels[range];
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }
            foreach (char c in label)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                {
                    return false;
                }
            }
        }
        return true;
    }
}
