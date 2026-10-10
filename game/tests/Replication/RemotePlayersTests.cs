using Godot;

using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Replication;
using Horyzont.Game.Testing;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Game.Tests.Replication;

public sealed class RemotePlayersTests : EngineTestBase
{
    private static readonly SnapshotFrame _frame = new(FrameRef.Of(WorldFactory.HomePlanet), 0);

    private static EntitySpawn Spawn(ulong entity, ulong owner, float x) =>
        new(new EntityId(entity), EntityKind.Player, 0, new PlayerId(owner), _frame,
            new EntityState(new EntityId(entity), (EntityStateFields)15, 0, 0, new System.Numerics.Vector3(x, 4000, 0),
                System.Numerics.Quaternion.Identity, System.Numerics.Vector3.Zero, 0), []);

    [EngineTest]
    public async Task RemotePlayers_SpawnRespawnDespawn_NodesFollowTheMirrorWithoutPhysics()
    {
        var network = new InMemoryNetwork(TimeProvider.System);
        using var transport = network.CreateTransport("client", TransportOptions.Default);
        var client = new ClientRuntime(transport, new ClientOptions { BuildHash = 1 }, false, TimeProvider.System, NullLoggerFactory.Instance);
        var mirror = new EntityMirror();
        var players = new RemotePlayers { Name = "RemotePlayers" };
        players.Initialize(client, mirror, render: true);
        Host.AddChild(players);
        players.OnServerHello(new ServerHello(WorldFactory.DefaultWorldId, default, 60, new PlayerId(1), new EntityId(1), [], [], []));

        mirror.Apply(Spawn(1, 1, 0)); // the local body gets no node
        mirror.Apply(Spawn(2, 2, 10));
        mirror.Apply(Spawn(3, 3, 20));
        EngineAssert.Equal(2, players.Count, "nodes of the other players");
        await NextFrame();
        EngineAssert.True(players.TryGetNode(new EntityId(2), out var node) && node is not null, "node of player 2");
        EngineAssert.True(Mathf.IsEqualApprox((float)node!.Position.X, 10f), "the node is placed with the spawn state");
        EngineAssert.Equal(0, node.FindChildren("*", "CollisionObject3D", true, false).Count, "no physics body");

        // A repeated spawn (resync, re-entering the range) refreshes the same node.
        mirror.Apply(Spawn(2, 2, 12));
        EngineAssert.Equal(2, players.Count, "no duplicate after a repeated spawn");
        await NextFrame();
        EngineAssert.True(Mathf.IsEqualApprox((float)node.Position.X, 12f), "the node moved to the new spawn state");

        mirror.Apply(new EntityDespawn(new EntityId(2)));
        EngineAssert.Equal(1, players.Count, "despawn frees the node");
        client.Session.Disconnect();
        players.OnDisconnected(Horyzont.Core.Protocol.DisconnectCode.Normal);
        EngineAssert.Equal(0, players.Count, "the session ending frees all nodes");
        players.GetParent()?.RemoveChild(players);
        players.Free();
    }
}
