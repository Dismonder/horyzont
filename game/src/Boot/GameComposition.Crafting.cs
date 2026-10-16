using Horyzont.Client.Session;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Server;

namespace Horyzont.Game.Boot;

/// <summary>
/// Crafting and stations on the client (K6-32). The inventory node owns the sessions; the composition tells it where the states of the
/// station blocks and the tick of the server come from, which only the composition knows (a host shares its world with the server, a remote
/// client reads the replication and the snapshots). The server side of crafting is registered with the other server participants.
/// </summary>
public sealed partial class GameComposition
{
    private ServerTickFollower? _serverTick;

    private void ConfigureClientCrafting()
    {
        if (Inventory is null || Client is null)
        {
            return;
        }

        var entities = Tools?.BlockEntities ?? RemoteTools?.BlockEntities;
        if (entities is null)
        {
            return;
        }

        _serverTick = new ServerTickFollower();
        Client.AddListener(_serverTick);
        Inventory.ConfigureStations(entities, ReadServerTick);
    }

    // Same source as the HUD (GameHudMetricsSource): a process that runs the server reads its tick, a remote client the latest snapshot.
    private bool ReadServerTick(out ulong tick)
    {
        if (Server is { RuntimeState: ServerRuntimeState.Running or ServerRuntimeState.SafeMode } server)
        {
            tick = server.State.CurrentTick.Value;
            return true;
        }

        tick = _serverTick?.Tick ?? 0;
        return Server is null && _serverTick?.Known == true;
    }

    private sealed class ServerTickFollower : IClientSessionListener
    {
        public ulong Tick { get; private set; }

        public bool Known { get; private set; }

        public void OnServerHello(ServerHello hello)
        {
            Tick = hello.ServerTick.Value;
            Known = true;
        }

        public void OnMessage(Message message)
        {
            if (message is Snapshot snapshot)
            {
                Tick = snapshot.ServerTick.Value;
                Known = true;
            }
        }

        public void OnDisconnected(Horyzont.Core.Protocol.DisconnectCode code) => Known = false;
    }
}
