using Horyzont.Client.Frames;
using Horyzont.Client.Replication;
using Horyzont.Client.Ships;
using Horyzont.Game.Client.Ships;
using Horyzont.Game.Constructs;
using Horyzont.Server;

namespace Horyzont.Game.Boot;

/// <summary>
/// Seats and piloting on the client (K5-25): the own seat (from the own reparents until K5-26 replicates it in <c>KindData</c>), the seat
/// keys of the tools and the pilot loop. The own seated body follows the ship at the render cursor, the pilot its prediction
/// (<see cref="ShipPilot.PoseOverride"/>, R7); the ship presentation of K5-26 must draw the ship from the same source.
/// </summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the own seat of the session; null without a replicating client.</summary>
    public OwnSeat? OwnSeat { get; private set; }

    /// <summary>Gets the pilot of the local player; null without constructs.</summary>
    public ShipPilotController? ShipPilot { get; private set; }

    private void ConfigureSeats(ClientFrames frames, ConstructMirror? mirror)
    {
        var own = new OwnSeat();
        Client!.AddListener(own);
        OwnSeat = own;
        LocalPlayer!.OwnSeat = own;
        if (Tools is { } tools)
        {
            tools.OwnSeat = own;
        }

        if (mirror is null)
        {
            return;
        }

        var pilot = new ShipPilotController(
            Client, own, mirror, frames, () => ClientStarSystem?.Model, ShipBlockDefinitions(BlockContentLoader.Load()), _env.Render,
            GameServerHooks.TickRate, _env.Loggers);
        ClientNode!.AddChild(pilot);
        Client.AddListener(pilot);
        ShipPilot = pilot;
        LocalPlayer.ShipPoseOverride = pilot.Pilot.PoseOverride;
    }
}
