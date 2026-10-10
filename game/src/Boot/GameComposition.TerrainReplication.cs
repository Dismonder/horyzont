using Godot;

using Horyzont.Client.Replication;
using Horyzont.Core;
using Horyzont.Core.Simulation;
using Horyzont.Game.Client;
using Horyzont.Game.Client.Terrain;
using Horyzont.Game.Client.Tools;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Server.Terrain;
using Horyzont.Server;
using Horyzont.Server.Replication;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Boot;

// K3-16: terrain replication. The server side registers the replicator of a host or dedicated server; the client side wires the cache,
// the replication, the readiness of the world, the edit prediction and the tools of a remote client (a host and a solo player share the
// terrain of the server and get none of it).
public sealed partial class GameComposition
{
    private IReplicationSendBudget? _terrainSendBudget;

    /// <summary>Gets the terrain replicator of the server; null in solo and client mode and before the world has started.</summary>
    public ServerTerrainReplicatorBinder? TerrainReplication { get; private set; }

    /// <summary>Gets the terrain replication of a remote client; null for a host, solo and dedicated server.</summary>
    public ClientTerrainReplicator? ClientTerrainReplication { get; private set; }

    /// <summary>Gets the tools of a remote client (K3-16); null elsewhere and with characters off.</summary>
    public ToolNode? RemoteTools { get; private set; }

    /// <summary>
    /// Gets the reliable replication budget of the process (27 000 B/s). One instance per process serves every client, as the shared
    /// bucket of X3 requires: entity replication (K3-15) takes the same one when both packages are integrated.
    /// </summary>
    internal IReplicationSendBudget TerrainSendBudget => _terrainSendBudget ??= new TokenBucketReplicationBudget(_env.Time);

    partial void ConfigureTerrainReplication()
    {
        ConfigureServerTerrainReplication();
        ConfigureClientTerrainReplication();
    }

    private void ConfigureServerTerrainReplication()
    {
        if (Server is null || TerrainFactory is null || _launch.Mode == ProcessMode.Solo)
        {
            return;
        }

        // Solo has no remote player. A host allows a far camera in a development build; a dedicated server follows its --dev flag.
        var freeCamera = _launch.Mode == ProcessMode.DedicatedServer ? _launch.Dev : _env.DevBuild;
        var binder = new ServerTerrainReplicatorBinder(
            TerrainFactory, _characters, TerrainSendBudget, freeCamera, _env.Time, _env.Loggers,
            BuildInfo.ComputeBuildHash(GeneratorProfiles.BuildHashComponent).ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
        binder.Register(Server);
        TerrainReplication = binder;
    }

    private void ConfigureClientTerrainReplication()
    {
        if (Client is not { } client || ClientNode is not { } node || _ownTerrain is not { } own || client.IsHostLocal)
        {
            return;
        }

        var cacheRoot = Path.Combine(_launch.DataDir ?? _env.UserDataDir, "cache");
        own.ConfigureCache(new OwnTerrainCacheSettings(cacheRoot, _env.Time, BuildInfo.ComputeBuildHash(GeneratorProfiles.BuildHashComponent), _env.Loggers));
        var replicator = new ClientTerrainReplicator(client, own, LocalPlayer, _env.Time, _env.Loggers.CreateLogger<ClientTerrainReplicator>());
        client.AddListener(replicator);
        node.TerrainReplicator = replicator;
        ClientTerrainReplication = replicator;
        if (LocalPlayer is not { } player)
        {
            return;
        }

        player.WorldReadiness = replicator;
        var prediction = new RemoteTerrainPrediction(own, _env.Time, _env.Loggers.CreateLogger<RemoteTerrainPrediction>());
        replicator.Attach(prediction);
        var raycast = new RemoteTerrainRaycast(own);
        var scene = GD.Load<PackedScene>(ToolsScene) ?? throw new InvalidOperationException($"Scene {ToolsScene} not found.");
        var tools = scene.Instantiate<ToolNode>();
        tools.Initialize(client, hello => new RemoteToolWorldView(hello, raycast), () => prediction, player, new GameRules(), _env.Loggers);
        if (_env.Render)
        {
            tools.AddChild(new TerrainToolPreview(tools));
            tools.AddChild(new ToolHud(tools));
        }

        node.AddChild(tools);
        RemoteTools = tools;
    }
}
