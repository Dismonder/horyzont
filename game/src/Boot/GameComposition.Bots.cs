using System.Globalization;

using Horyzont.Bots.Scripting;
using Horyzont.Game.Client.Bots;

namespace Horyzont.Game.Boot;

// K4-19: the bot mode of a remote client (--connect --dev --bot <seed>). The script of the bot libraries replaces the keyboard and the
// mouse of the local player and of the remote tools; everything else is the client of a player.
public sealed partial class GameComposition
{
    /// <summary>Gets the driver of the bot mode; null for a player.</summary>
    public BotDriver? Bot { get; private set; }

    partial void ConfigureBots()
    {
        if (_launch.BotSeed is not { } seed || LocalPlayer is not { } player || ClientNode is not { } node)
        {
            return;
        }

        var control = _launch.BotControlSeconds;
        var schedule = new BotSchedule
        {
            ControlPeriodTicks = (long)control * BotSchedule.TicksPerSecond,
            QuietTicks = (control >= 300 ? 60L : 20L) * BotSchedule.TicksPerSecond,
            DrainTicks = (control >= 300 ? 20L : 10L) * BotSchedule.TicksPerSecond,
        };
        var epoch = _launch.BotEpochUnixMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(_launch.BotEpochUnixMilliseconds)
            : DateTimeOffset.UtcNow.AddSeconds(2);
        var output = _launch.BotOut ?? Path.GetFullPath("bot-" + seed.ToString(CultureInfo.InvariantCulture));
        var driver = new BotDriver();
        driver.Initialize(seed, _launch.BotRole, schedule, epoch, output, player, ClientReplication, _ownTerrain, ClientTerrainReplication);
        player.InputSource = driver.Input!;
        if (RemoteTools is { } tools)
        {
            tools.InputSource = driver.Input!;
        }

        node.AddChild(driver);
        Bot = driver;
    }
}
