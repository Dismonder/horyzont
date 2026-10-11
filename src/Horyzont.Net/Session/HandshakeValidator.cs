using System.Security.Cryptography;
using System.Text;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Transport;

namespace Horyzont.Net.Session;

/// <summary>Decides whether a decoded ClientHello with the current protocol version is accepted.</summary>
public static class HandshakeValidator
{
    /// <summary>
    /// Checks, in this order: build hash, password (constant-time comparison of SHA-256 digests), Steam identity
    /// (the SteamID of a Steam endpoint must match the claim, and a Steam claim needs a Steam endpoint), bans, an identity already connected, the player limit.
    /// </summary>
    /// <param name="hello">The client's hello.</param>
    /// <param name="context">Server state.</param>
    /// <returns>The reject code, or null when the client is accepted.</returns>
    public static HandshakeRejectCode? Validate(ClientHello hello, HandshakeContext context)
    {
        ArgumentNullException.ThrowIfNull(hello);
        var options = context.Options ?? throw new ArgumentException("Options are required.", nameof(context));

        if (hello.BuildHash != options.BuildHash)
        {
            return HandshakeRejectCode.WrongBuild;
        }

        if (options.Password.Length > 0 && !PasswordsMatch(options.Password, hello.Password))
        {
            return HandshakeRejectCode.BadPassword;
        }

        if (context.RemoteEndpoint is SteamEndpoint steam && hello.Identity != new PlatformIdentity(IdentityPlatform.Steam, steam.SteamId))
        {
            return HandshakeRejectCode.IdentityMismatch;
        }

        // A Steam identity is proven only by the Steam P2P endpoint: over UDP or in memory it would be a plain claim.
        if (hello.Identity.Platform == IdentityPlatform.Steam && context.RemoteEndpoint is not SteamEndpoint)
        {
            return HandshakeRejectCode.IdentityMismatch;
        }

        if (options.BannedIdentities.Contains(hello.Identity))
        {
            return HandshakeRejectCode.Banned;
        }

        if (context.IdentityConnected)
        {
            return HandshakeRejectCode.AlreadyConnected;
        }

        if (context.PlayerCount >= options.MaxPlayers)
        {
            return HandshakeRejectCode.ServerFull;
        }

        return null;
    }

    private static bool PasswordsMatch(string expected, string actual)
    {
        Span<byte> expectedHash = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> actualHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(expected), expectedHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(actual ?? string.Empty), actualHash);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
