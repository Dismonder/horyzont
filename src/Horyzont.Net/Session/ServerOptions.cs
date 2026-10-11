using Horyzont.Core.Ids;

namespace Horyzont.Net.Session;

/// <summary>Settings of a <see cref="ServerSession"/>.</summary>
public sealed record ServerOptions
{
    /// <summary>Gets the build hash clients must present (<see cref="Core.BuildInfo.ComputeBuildHash"/>).</summary>
    public required ulong BuildHash { get; init; }

    /// <summary>Gets the most players in the states Loading and InGame together.</summary>
    public int MaxPlayers { get; init; } = 8;

    /// <summary>
    /// Gets the most connections outside the states Loading and InGame (awaiting ClientHello or closing). Further
    /// connections are refused with <see cref="Core.Protocol.HandshakeRejectCode.ServerFull"/> before any per-connection
    /// state is allocated, so the server never holds more than <see cref="MaxPlayers"/> + this many connections.
    /// </summary>
    public int MaxPendingConnections { get; init; } = 16;

    /// <summary>Gets the server password; empty means no password.</summary>
    public string Password { get; init; } = "";

    /// <summary>Gets the identities refused with <see cref="Core.Protocol.HandshakeRejectCode.Banned"/>.</summary>
    public IReadOnlySet<PlatformIdentity> BannedIdentities { get; init; } = new HashSet<PlatformIdentity>();

    /// <summary>Gets how long a new connection may take to send a valid ClientHello.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets the silence from a joined player that ends the session.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the delay between the final reliable message and closing the transport connection.</summary>
    public TimeSpan CloseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets the default view range of interest subjects, in metres.</summary>
    public float InterestRadiusMeters { get; init; } = 500f;

    /// <summary>Gets the extra distance a visible subject may move away before it leaves the interest set, in metres.</summary>
    public float InterestHysteresisMeters { get; init; } = 50f;

    /// <summary>Gets the most inputs buffered per player.</summary>
    public int InputBufferCapacity { get; init; } = 8;

    /// <summary>Pilot inputs buffered per player (K5-25): the delay of two slots plus jitter.</summary>
    public int ShipInputBufferCapacity { get; init; } = 16;

    /// <summary>Gets the reliable messages accepted per second from one peer (burst: two seconds).</summary>
    public int ReliableMessagesPerSecond { get; init; } = 200;

    /// <summary>Gets the unreliable messages accepted per second from one peer (burst: two seconds).</summary>
    public int UnreliableMessagesPerSecond { get; init; } = 120;
}
