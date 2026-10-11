using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Server.Characters;
using Horyzont.Server;
using Horyzont.Server.Replication;
using Horyzont.Server.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Server.Terrain;

/// <summary>
/// Connects the <see cref="ServerTerrainReplicator"/> to a <see cref="ServerRuntime"/> that has not started yet: the simulation,
/// the session and the terrain only exist after the world loads, so the replicator is made on the first tick, the first ready player or
/// the first terrain message of a client. Register it with <see cref="Register"/> before <c>StartAsync</c>. A server without remote
/// players (solo) does the work of an empty loop and nothing more.
/// </summary>
public sealed class ServerTerrainReplicatorBinder : IServerTickParticipant, IServerPlayerObserver, IServerClientMessageHandler
{
    private readonly VoxelServerTerrainFactory _terrainFactory;
    private readonly ServerCharactersBinder? _characters;
    private readonly IReplicationSendBudget _budget;
    private readonly bool _freeCameraAllowed;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;
    private readonly string _buildHash;
    private ServerRuntime? _runtime;
    private ServerTerrainReplicator? _replicator;
    private Host? _host;

    /// <summary>Creates the binder.</summary>
    /// <param name="terrainFactory">Factory whose terrain is replicated (read when the world has started).</param>
    /// <param name="characters">Characters, for the feet of the players; null when the server has none.</param>
    /// <param name="budget">The reliable replication budget shared with entity replication.</param>
    /// <param name="freeCameraAllowed">Whether clients may place the visual viewer far from their character.</param>
    /// <param name="time">Clock.</param>
    /// <param name="loggers">Loggers.</param>
    /// <param name="buildHash">Build label for the log.</param>
    public ServerTerrainReplicatorBinder(
        VoxelServerTerrainFactory terrainFactory, ServerCharactersBinder? characters, IReplicationSendBudget budget, bool freeCameraAllowed,
        TimeProvider time, ILoggerFactory loggers, string buildHash)
    {
        ArgumentNullException.ThrowIfNull(terrainFactory);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(loggers);
        _terrainFactory = terrainFactory;
        _characters = characters;
        _budget = budget;
        _freeCameraAllowed = freeCameraAllowed;
        _time = time;
        _loggers = loggers;
        _buildHash = buildHash;
    }

    /// <summary>Gets the replicator once the world has started; null before.</summary>
    public ServerTerrainReplicator? Replicator => _replicator;

    /// <summary>Registers the binder as tick participant, player observer and message handler of a server runtime.</summary>
    /// <param name="runtime">The runtime, not started yet.</param>
    public void Register(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        runtime.AddTickParticipant(this);
        runtime.AddPlayerObserver(this);
        runtime.AddClientMessageHandler(this);
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime) => Ensure(runtime).OnTick(runtime);

    /// <inheritdoc />
    public void OnShutdown(ServerRuntime runtime) => _replicator?.OnShutdown(runtime);

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player) => Ensure(_runtime ?? throw new InvalidOperationException("The binder is not registered.")).OnPlayerReady(player);

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason) => _replicator?.OnPlayerLeaving(player, reason);

    /// <inheritdoc />
    public bool Handle(PlayerId player, Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message is not (TerrainCacheReport or TerrainChecksums or TerrainView))
        {
            return false;
        }

        return Ensure(_runtime ?? throw new InvalidOperationException("The binder is not registered.")).Handle(player, message);
    }

    private ServerTerrainReplicator Ensure(ServerRuntime runtime)
    {
        if (_replicator is not null)
        {
            return _replicator;
        }

        var terrain = _terrainFactory.Terrain ?? throw new InvalidOperationException("The server terrain does not exist yet.");
        _host = new Host(runtime, _characters, _freeCameraAllowed);
        _replicator = new ServerTerrainReplicator(
            _host, terrain, runtime.State, _budget, _time, _loggers.CreateLogger<ServerTerrainReplicator>(), _buildHash);
        runtime.Simulation.Applied += _replicator.OnOperationApplied;
        return _replicator;
    }

    private sealed class Host(ServerRuntime runtime, ServerCharactersBinder? characters, bool freeCameraAllowed) : ITerrainReplicationHost
    {
        public bool FreeCameraAllowed => freeCameraAllowed;

        public bool IsLocalPlayer(PlayerId player) => runtime.IsLocalPlayer(player);

        public bool TryGetFeet(PlayerId player, out PlanetId planet, out Vector3 feet)
        {
            if (characters?.Characters is { } active && active.TryGetState(player, out var state))
            {
                planet = state.Planet;
                feet = state.Feet;
                return true;
            }

            planet = default;
            feet = default;
            return false;
        }

        public void Send(PlayerId player, Message message) => runtime.Session.Send(player, message);
    }
}
