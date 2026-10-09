using Horyzont.Core.Simulation;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Persistence;
using Horyzont.Persistence.Faults;

namespace Horyzont.Server;

/// <summary>Settings of a <see cref="ServerRuntime"/>.</summary>
public sealed record ServerRuntimeOptions
{
    /// <summary>Gets the world file (<c>saves/&lt;WorldId&gt;/world.db</c>).</summary>
    public required string WorldPath { get; init; }

    /// <summary>Gets the directory of the backups; null means <c>backups</c> next to the world file.</summary>
    public string? BackupsDirectory { get; init; }

    /// <summary>Gets the session settings.</summary>
    public required ServerOptions ServerOptions { get; init; }

    /// <summary>Gets the writer settings.</summary>
    public required PersistenceOptions PersistenceOptions { get; init; }

    /// <summary>Gets the endpoints the transport listens on (one per inner transport of a composite).</summary>
    public required IReadOnlyList<TransportEndpoint> Listen { get; init; }

    /// <summary>Gets the endpoint under which the local client of a host connects; <see cref="ServerRuntime.IsLocalPlayer"/> compares with it.</summary>
    public TransportEndpoint? LocalClientEndpoint { get; init; }

    /// <summary>Gets the interval between checkpoints.</summary>
    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets how long step 2 of the shutdown pumps the session before it goes on.</summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets how long the startup checkpoint may take.</summary>
    public TimeSpan StartupCheckpointTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets how long the checkpoint of the shutdown may take.</summary>
    public TimeSpan ShutdownCheckpointTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets how long <c>FlushAsync</c> may take during the shutdown.</summary>
    public TimeSpan FlushTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets how long <c>DisposeAsync</c> of the writer may take during the shutdown.</summary>
    public TimeSpan DisposeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets how long <see cref="IServerTerrain.ShutdownAsync"/> may take during the shutdown.</summary>
    public TimeSpan TerrainShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the block type keys of the game content; missing keys are appended to the world registries.</summary>
    public IReadOnlyList<string> BlockTypeKeys { get; init; } = [];

    /// <summary>Gets the material keys of the game content; missing keys are appended to the world registries.</summary>
    public IReadOnlyList<string> MaterialKeys { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether a journal tail written with other terrain semantics is dropped (after a <c>pre-recover</c> backup)
    /// instead of opening the world in safe mode: the owner's explicit <c>--recover-from-checkpoint</c>.
    /// </summary>
    public bool RecoverFromCheckpoint { get; init; }

    /// <summary>Gets the rules of the simulation; <see cref="Core.Simulation.GameRules"/> defaults, identical on server and clients of one build.</summary>
    public GameRules Rules { get; init; } = new();

    /// <summary>Gets the failure injection of the writer; <see cref="NoFaults.Instance"/> in production.</summary>
    public IPersistenceFaults Faults { get; init; } = NoFaults.Instance;

    /// <summary>Gets a wrapper for the journal sink (tests observe appends); null in production.</summary>
    public Func<IJournalSink, IJournalSink>? JournalSinkDecorator { get; init; }
}
