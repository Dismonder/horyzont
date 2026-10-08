namespace Horyzont.Net.Session;

/// <summary>Settings of a <see cref="ClientSession"/>.</summary>
public sealed record ClientOptions
{
    /// <summary>Gets the build hash sent in ClientHello (<see cref="Core.BuildInfo.ComputeBuildHash"/>).</summary>
    public required ulong BuildHash { get; init; }

    /// <summary>Gets the interval of Ping messages from the Loading state on.</summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets how long the client waits from Connect for ServerHello or HandshakeReject.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
