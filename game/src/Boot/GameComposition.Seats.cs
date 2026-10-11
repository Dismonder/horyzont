using Horyzont.Client.Frames;
using Horyzont.Client.Replication;
using Horyzont.Client.Ships;
using Horyzont.Game.Client.Ships;
using Horyzont.Game.Constructs;
using Horyzont.Server;

namespace Horyzont.Game.Boot;

/// <summary>
/// Seats and piloting on the client (K5-25): the own seat (from the own reparents until K5-26 replicates it in <c>KindData</c>), the seat
/// keys of the tools and the pilot loop. The own seated body follows the ship at the render cursor, the pilot too until K5-26 draws
/// the piloted hull from its prediction (<see cref="ShipPilot.PoseOverride"/>, R7): body, camera and hull share one pose source.
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

        // R7: the hull is still drawn from the render cursor (ConstructMirror), so the pilot body and camera use the render cursor too;
        // with the prediction pose they would drift from the hull they sit in.
        // TODO(K5-26): when the construct presentation draws the piloted hull from the prediction, set
        // LocalPlayer.ShipPoseOverride = pilot.Pilot.PoseOverride here (one pose source for hull, body and camera).
        LocalPlayer.ShipPoseOverride = null;
    }
}
