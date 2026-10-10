using Horyzont.Bots.Scripting;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Net.Transport;

namespace Horyzont.Bots.Client;

/// <summary>Settings of one bot.</summary>
public sealed record BotClientOptions
{
    /// <summary>Gets the display name sent in the handshake.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the platform identity (a development identity; unique per bot).</summary>
    public required PlatformIdentity Identity { get; init; }

    /// <summary>Gets the server to connect to.</summary>
    public required TransportEndpoint Server { get; init; }

    /// <summary>Gets the build hash of the handshake (the same as the server's).</summary>
    public required ulong BuildHash { get; init; }

    /// <summary>Gets the seed of the script.</summary>
    public ulong Seed { get; init; } = 1;

    /// <summary>Gets the role.</summary>
    public BotRole Role { get; init; } = BotRole.Builder;

    /// <summary>Gets the timetable shared by all bots.</summary>
    public BotSchedule Schedule { get; init; } = new();

    /// <summary>Gets the game rules of the build (the same defaults as the server's).</summary>
    public GameRules Rules { get; init; } = new();

    /// <summary>Gets the wait before the bot connects again after it was disconnected.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);
}
