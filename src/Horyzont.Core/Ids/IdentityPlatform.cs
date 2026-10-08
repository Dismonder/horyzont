namespace Horyzont.Core.Ids;

/// <summary>Platform that issued a <see cref="PlatformIdentity"/>. New platforms are appended at the end.</summary>
public enum IdentityPlatform : byte
{
    /// <summary>Random number stored in the client profile (tests, local network).</summary>
    Dev = 1,

    /// <summary>SteamID64.</summary>
    Steam = 2,
}
