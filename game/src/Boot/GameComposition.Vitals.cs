using Horyzont.Client.Survival;
using Horyzont.Game.Client.Survival;
using Horyzont.Server;

namespace Horyzont.Game.Boot;

/// <summary>
/// The client side of the character state and eating (K6-33): the vitals the server sends (message 44) feed the exhaustion filter of the local player and the vitals HUD.
/// The server side (the vitals service, the filter of the server characters) is registered with the other server participants. Eating from the hotbar is wired by the
/// tools, which build their controller at the server hello.
/// </summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the vitals of the local player as the server told them; null without a client character.</summary>
    public ClientVitals? Vitals { get; private set; }

    /// <summary>Gets the vitals HUD; null without a rendering client or inventory.</summary>
    public VitalsHud? VitalsHud { get; private set; }

    private ClientVitals CreateClientVitals() => Vitals = new ClientVitals();

    private void ConfigureClientVitals()
    {
        if (Vitals is not { } vitals || LocalPlayer is not { } player)
        {
            return;
        }

        // The same filter the server applies to the body of this player (StaminaGate), so the prediction does not run where the server stands still.
        player.ExhaustedCheck = () => vitals.Exhausted;
        if (!_env.Render || ClientNode is not { } clientNode || Inventory is not { } inventory)
        {
            return;
        }

        var source = new VitalsHudSource(
            vitals, inventory.Progress, inventory.Inventory, inventory.VisualOf, inventory.ItemName, ReadServerTick, tickRate: GameServerHooks.TickRate);
        VitalsHud = new VitalsHud(source.Build);
        clientNode.AddChild(VitalsHud);
    }
}
