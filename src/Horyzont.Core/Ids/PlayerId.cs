namespace Horyzont.Core.Ids;

/// <summary>
/// Internal identifier of a player in a world, assigned by the server from 1 (<see cref="PlayerIdAllocator"/>).
/// Never a platform identity such as a SteamID; that is <see cref="PlatformIdentity"/>.
/// </summary>
/// <param name="Value">Numeric value; 0 is never assigned.</param>
public readonly record struct PlayerId(ulong Value)
{
    /// <summary>The unassigned identifier (0).</summary>
    public static PlayerId None => default;

    /// <summary>True for <see cref="None"/>.</summary>
    public bool IsNone => Value == 0;
}
