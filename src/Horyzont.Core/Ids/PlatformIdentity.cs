namespace Horyzont.Core.Ids;

/// <summary>
/// Identity of a person on a platform, used only to link a connection to a <see cref="PlayerId"/>,
/// for bans and for administrators. Never stored in the journal in place of a <see cref="PlayerId"/>.
/// </summary>
/// <param name="Platform">Issuing platform.</param>
/// <param name="Value">Platform-specific value (for Steam the SteamID64).</param>
public readonly record struct PlatformIdentity(IdentityPlatform Platform, ulong Value);
