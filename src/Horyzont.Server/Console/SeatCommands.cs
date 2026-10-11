using System.Globalization;
using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Server.Console;

/// <summary>
/// The dev console command <c>seat</c> (K5-25 decision 22): <c>seat &lt;player&gt; enter &lt;construct&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;</c>,
/// <c>seat &lt;player&gt; exit</c>, <c>seat &lt;player&gt; evict &lt;construct&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;</c>. The intents 11, 12 and 16
/// are submitted on behalf of the player through <see cref="ServerSimulation.Submit"/> (the same validators), aimed from the eye of the
/// player at the centre of the seat cell. Result line (fixed format): <c>SEAT player=… result=accepted|rejected code=…</c>.
/// </summary>
public static class SeatCommands
{
    /// <summary>Usage text.</summary>
    public const string Usage = "usage: seat <player> enter <construct> <x> <y> <z> | seat <player> exit | seat <player> evict <construct> <x> <y> <z>";

    /// <summary>Executes the command.</summary>
    /// <param name="simulation">The simulation.</param>
    /// <param name="parts">The split line, starting with <c>seat</c>.</param>
    /// <returns>The result line.</returns>
    public static string Execute(ServerSimulation simulation, string[] parts)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length < 3 || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var playerId))
        {
            return Usage;
        }

        var player = new PlayerId(playerId);
        if (!simulation.State.Players.TryGet(player, out var state))
        {
            return string.Create(CultureInfo.InvariantCulture, $"unknown player {playerId}");
        }

        var verb = parts[2].ToLowerInvariant();
        Intent? intent = verb switch
        {
            "exit" when parts.Length == 3 => state.Seat is not null && state.Frame.Kind == FrameKind.Construct
                ? new SeatExitIntent(new EntityId(state.Frame.Id))
                : new SeatExitIntent(default),
            "enter" or "evict" when parts.Length == 7 && TryTarget(parts, out var construct, out var cell) =>
                Aim(simulation, state, construct, cell, verb == "enter"),
            _ => null,
        };
        if (intent is null)
        {
            return Usage;
        }

        var result = simulation.Submit(player, intent);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"SEAT player={playerId} result={(result.IsAccepted ? "accepted" : "rejected")} code={(int)result.Reason}");
    }

    private static bool TryTarget(string[] parts, out EntityId construct, out CellPos cell)
    {
        construct = default;
        cell = default;
        if (!ulong.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || !int.TryParse(parts[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[5], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[6], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z))
        {
            return false;
        }

        construct = new EntityId(id);
        cell = new CellPos(x, y, z);
        return true;
    }

    private static Intent Aim(ServerSimulation simulation, Core.World.PlayerState player, EntityId construct, CellPos cell, bool enter)
    {
        var origin = Vector3.Zero;
        var direction = Vector3.UnitY;
        var center = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        if (IntentActor.TryGetEye(simulation.State, simulation.Rules, player, out _, out var eye)
            && simulation.State.TryToPlanetFrame(FrameRef.Of(construct), center, out _, out var target)
            && Vector3.Distance(target, eye) > 1e-4f)
        {
            origin = eye;
            direction = Vector3.Normalize(target - eye);
        }

        return enter ? new SeatEnterIntent(construct, cell, origin, direction) : new SeatEvictIntent(construct, cell, origin, direction);
    }
}
