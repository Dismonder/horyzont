using Horyzont.Client.Ships;
using Horyzont.Server;
using Horyzont.Server.Ships;

namespace Horyzont.Game.Boot;

/// <summary>
/// Ship replication (K5-26): the server sends <c>ShipStatus</c> (31) at 2 Hz in phase Compose to the players in a ship (also on the
/// dedicated server); the client keeps the newest status per ship for the flight HUD.
/// </summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the ship statuses of the client; null without a client.</summary>
    public ClientShipStatus? ClientShipStatus { get; private set; }

    private void ConfigureShipStatusServer(ServerRuntime server)
    {
        var flight = Flight;
        server.AddTickParticipant(new ShipStatusSender(() => flight?.Flight));
    }

    private void ConfigureShipStatusClient()
    {
        ClientShipStatus = new ClientShipStatus();
        Client!.AddListener(ClientShipStatus);
    }
}
