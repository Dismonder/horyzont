using System.Globalization;
using System.Text;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Frames;
using Horyzont.Server.Ships;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Console;

/// <summary>
/// Executes the commands of the server console (save, stop, players, kick, help) on the main thread. With development commands
/// enabled (the <c>--dev</c> launch flag) it also knows <c>dev-op</c>, which the smoke test uses to change the world, and <c>frame</c>
/// (K5-20: list frames, reparent an entity keeping its world pose) when frame transitions are available.
/// </summary>
public sealed class ConsoleCommandProcessor
{
    /// <summary>Longest accepted input line.</summary>
    public const int MaxLineLength = 256;

    private const string Help =
        "commands:\n  save            write a checkpoint now (also leaves safe mode after a write error)\n  stop            save and shut the server down\n  players         list the players in game\n  kick <id>       disconnect a player by player id\n  help            this list";

    private const string FrameUsage = "usage: frame list | frame reparent <entity> <system|planet:id|construct:id> [seat x y z]";

    private readonly ServerRuntime _runtime;
    private readonly ILogger<ConsoleCommandProcessor> _logger;
    private readonly bool _devCommands;
    private readonly Func<FrameTransitions?>? _frames;

    /// <summary>Initializes a new instance of the <see cref="ConsoleCommandProcessor"/> class.</summary>
    /// <param name="runtime">The server the commands act on.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="devCommands">Whether the development commands <c>dev-op</c> and <c>frame</c> are available.</param>
    /// <param name="frames">Frame transitions of the running world (resolved per command; null before the world starts).</param>
    public ConsoleCommandProcessor(
        ServerRuntime runtime, ILogger<ConsoleCommandProcessor> logger, bool devCommands = false, Func<FrameTransitions?>? frames = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(logger);
        _runtime = runtime;
        _logger = logger;
        _devCommands = devCommands;
        _frames = frames;
    }

    /// <summary>Executes one input line; never throws for bad input.</summary>
    /// <param name="line">The line, as typed.</param>
    /// <returns>The text to print; empty for an empty line.</returns>
    public string Execute(string line)
    {
        if (line is null)
        {
            return string.Empty;
        }

        if (line.Length > MaxLineLength)
        {
            return string.Create(CultureInfo.InvariantCulture, $"line too long (at most {MaxLineLength} characters)");
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var command = parts[0].ToLowerInvariant();
        _logger.ConsoleCommand(command);
        return command switch
        {
            "save" => Save(),
            "stop" => Stop(),
            "players" => Players(),
            "kick" => Kick(parts),
            "ship" => Ship(parts),
            "dev-op" when _devCommands => DevOp(),
            "frame" when _devCommands && _frames is not null => Frame(parts),
            _ => Help,
        };
    }

    private static string FormatIdentity(PlatformIdentity identity) =>
        string.Create(CultureInfo.InvariantCulture, $"{identity.Platform}:{identity.Value}");

    private string Save()
    {
        if (_runtime.RuntimeState is not (ServerRuntimeState.Running or ServerRuntimeState.SafeMode))
        {
            return "the server is not running";
        }

        _runtime.RequestCheckpoint();
        return "save queued";
    }

    private string Stop()
    {
        _runtime.RequestShutdown();
        return "stopping";
    }

    private string DevOp()
    {
        if (_runtime.RuntimeState is not (ServerRuntimeState.Running or ServerRuntimeState.SafeMode))
        {
            return "the server is not running";
        }

        // A journaled change of the world with no gameplay behind it: a new offline player (join and leave of a made-up identity).
        var simulation = _runtime.Simulation;
        var identity = new PlatformIdentity(IdentityPlatform.Dev, 0xDE00_0000UL + _runtime.State.PlayerIds.PeekNext().Value);
        var join = simulation.PreparePlayerJoin(identity, "dev-op");
        simulation.ApplyServerOperation(join);
        simulation.ApplyServerOperation(new PlayerLeaveOp(join.Player, join.Frame, join.ParentSeq, join.Position, join.BodyRotation, 0f));
        return string.Create(CultureInfo.InvariantCulture, $"dev-op applied: player {join.Player.Value} added offline");
    }

    private string Players()
    {
        if (_runtime.RuntimeState is not (ServerRuntimeState.Running or ServerRuntimeState.SafeMode))
        {
            return "the server is not running";
        }

        var session = _runtime.Session;
        if (session.PlayersInGame.Count == 0)
        {
            return "0 players";
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{session.PlayersInGame.Count} players");
        foreach (var player in session.PlayersInGame)
        {
            if (_runtime.State.Players.TryGet(player, out var state))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n  {player.Value}\t{state.DisplayName}\t{FormatIdentity(state.Identity)}\t{session.GetRtt(player).TotalMilliseconds:0} ms");
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Prints one line <c>SHIP id=… mode=… mass=…kg com=(x,y,z) thrust=+X:…,-X:…,+Y:…,-Y:…,+Z:…,-Z:… torque=… fuel=… cockpits=…
    /// engines=… seats=…</c>. The format is fixed: tests and the manual procedure of K5-23 compare it.
    /// </summary>
    private string Ship(string[] parts)
    {
        if (parts.Length == 4 && parts[1].Equals("fuel", StringComparison.OrdinalIgnoreCase))
        {
            return ShipFuel(parts[2], parts[3]);
        }

        if (parts.Length == 4 && parts[1].Equals("pilot", StringComparison.OrdinalIgnoreCase) && _devCommands)
        {
            return ShipPilot(parts[2], parts[3]);
        }

        if (parts.Length != 2 || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return "usage: ship <construct id>";
        }

        if (_runtime.RuntimeState is not (ServerRuntimeState.Running or ServerRuntimeState.SafeMode))
        {
            return "the server is not running";
        }

        var construct = new EntityId(id);
        if (!_runtime.State.Constructs.TryGet(construct, out var state))
        {
            return string.Create(CultureInfo.InvariantCulture, $"unknown construct {id}");
        }

        var stats = _runtime.ShipStats.TryGet(construct, out var cached)
            ? cached
            : ShipStatsCalculator.Compute(state, _runtime.Simulation.Content);
        var line = FormatShip(construct, state.Mode, stats);
        return _runtime.TryGetTickParticipant<ShipFlightHost>(out var host) && host.Flight is { } flight
            && flight.TryGetStatus(construct, out var status)
            ? line + " " + FormatFlight(status, _runtime.State.ShipStates.TryGet(construct, out var record) ? record.LinearVelocity.Length() : 0f)
            : line;
    }

    /// <summary>Formats the flight part of the <c>ship</c> line (K5-24; fixed format): <c>fuel=… v=… limit=… parked=… gate=…</c>.</summary>
    /// <param name="status">The flight status.</param>
    /// <param name="speed">Speed in m/s.</param>
    /// <returns>The text.</returns>
    public static string FormatFlight(in ShipFlightStatus status, float speed)
    {
        var gate = (status.Flags & ShipFlightFlags.TerrainUnavailable) != 0 ? "unavailable"
            : (status.Flags & ShipFlightFlags.TerrainHold) != 0 ? "hold"
            : "ready";
        var parked = (status.Flags & ShipFlightFlags.Parked) != 0 ? "yes" : "no";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"fuel={status.Fuel:0.#}/{status.FuelCapacity:0.#} v={speed:0.#} limit={status.SpeedLimit:0.#} parked={parked} gate={gate}");
    }

    /// <summary><c>ship fuel &lt;id&gt; &lt;units&gt;</c>: adds fuel through the server operation 21 (journaled), clipped to the tanks.</summary>
    private string ShipFuel(string idText, string unitsText)
    {
        if (!ulong.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || !float.TryParse(unitsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var units) || !float.IsFinite(units) || units <= 0)
        {
            return "usage: ship fuel <construct id> <units>";
        }

        if (_runtime.RuntimeState is not ServerRuntimeState.Running)
        {
            return "the server is not running";
        }

        var ship = new EntityId(id);
        if (!_runtime.ShipStats.TryGet(ship, out var stats))
        {
            return string.Create(CultureInfo.InvariantCulture, $"construct {id} is not a ship");
        }

        _runtime.Simulation.ApplyServerOperation(new ShipFuelAddOp(ship, units, stats.FuelCapacity));
        var fuel = _runtime.State.ShipStates.TryGet(ship, out var record) ? record.Fuel : 0f;
        return string.Create(CultureInfo.InvariantCulture, $"ship {id} fuel={fuel:0.#}/{stats.FuelCapacity:0.#}");
    }

    /// <summary><c>ship pilot &lt;id&gt; &lt;planet:id|stop&gt;</c> (development): the transit pilot flies the ship to a body.</summary>
    private string ShipPilot(string idText, string target)
    {
        if (!ulong.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return "usage: ship pilot <construct id> <planet:id|stop>";
        }

        if (!_runtime.TryGetTickParticipant<ShipFlightHost>(out var host) || host.Dev is not { } dev)
        {
            return "development pilots are not running";
        }

        var ship = new EntityId(id);
        if (target.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            return dev.Stop(ship) ? string.Create(CultureInfo.InvariantCulture, $"ship {id} pilot stopped") : string.Create(CultureInfo.InvariantCulture, $"ship {id} has no pilot");
        }

        if (!target.StartsWith("planet:", StringComparison.OrdinalIgnoreCase)
            || !ulong.TryParse(target.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var planet))
        {
            return "usage: ship pilot <construct id> <planet:id|stop>";
        }

        if (!_runtime.ShipStats.TryGet(ship, out _))
        {
            return string.Create(CultureInfo.InvariantCulture, $"construct {id} is not a ship");
        }

        return dev.Start(ship, new PlanetId(planet))
            ? string.Create(CultureInfo.InvariantCulture, $"ship {id} pilot to planet {planet}")
            : string.Create(CultureInfo.InvariantCulture, $"unknown planet {planet}");
    }

    /// <summary>Formats the <c>ship</c> line.</summary>
    /// <param name="construct">Construct id.</param>
    /// <param name="mode">Construct mode.</param>
    /// <param name="stats">Its stats.</param>
    /// <returns>The line.</returns>
    public static string FormatShip(EntityId construct, ConstructMode mode, ShipStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        var t = stats.Thrust;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"SHIP id={construct.Value} mode={mode} mass={stats.MassKg:0.##}kg com=({stats.CenterOfMass.X:0.##},{stats.CenterOfMass.Y:0.##},{stats.CenterOfMass.Z:0.##}) thrust=+X:{t.PosX:0.##},-X:{t.NegX:0.##},+Y:{t.PosY:0.##},-Y:{t.NegY:0.##},+Z:{t.PosZ:0.##},-Z:{t.NegZ:0.##} torque={stats.TorqueNewtonMeters:0.##} fuel={stats.FuelCapacity:0.##} cockpits={stats.Cockpits} engines={stats.Engines} seats={stats.Seats.Count}");
    }

    private string Kick(string[] parts)
    {
        if (parts.Length != 2 || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return "usage: kick <player id>";
        }

        if (_runtime.RuntimeState is not (ServerRuntimeState.Running or ServerRuntimeState.SafeMode))
        {
            return "the server is not running";
        }

        var player = new PlayerId(id);
        if (!_runtime.Session.PlayersInGame.Contains(player))
        {
            return string.Create(CultureInfo.InvariantCulture, $"unknown player {id}");
        }

        _runtime.Session.Kick(player, DisconnectCode.Kicked);
        return string.Create(CultureInfo.InvariantCulture, $"kicked {id}");
    }

    private static string FormatFrame(FrameRef frame) => frame.Kind switch
    {
        FrameKind.System => string.Create(CultureInfo.InvariantCulture, $"system:{frame.Id}"),
        FrameKind.Planet => string.Create(CultureInfo.InvariantCulture, $"planet:{frame.Id}"),
        FrameKind.Construct => string.Create(CultureInfo.InvariantCulture, $"construct:{frame.Id}"),
        _ => "none",
    };

    private static bool TryParseFrame(string text, out FrameRef frame)
    {
        frame = default;
        if (text == "system")
        {
            frame = FrameRef.Of(new SystemId(1));
            return true;
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || !ulong.TryParse(text.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        var kind = text[..colon] switch
        {
            "system" => FrameKind.System,
            "planet" => FrameKind.Planet,
            "construct" => FrameKind.Construct,
            _ => (FrameKind)0,
        };
        frame = new FrameRef(kind, id);
        return kind != 0;
    }

    private static string FormatWorld(FrameGraph graph, FrameRef frame, in PoseD local)
    {
        if (!graph.TryGetWorldPose(frame, out var world))
        {
            return "-";
        }

        var p = FrameMath.TransformPoint(world, local.Position);
        return string.Create(CultureInfo.InvariantCulture, $"{p.X:F3},{p.Y:F3},{p.Z:F3}");
    }

    private static string FrameReparent(FrameTransitions frames, string[] parts)
    {
        CellPos? seat = null;
        if (!ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || !TryParseFrame(parts[3].ToLowerInvariant(), out var frame))
        {
            return FrameUsage;
        }

        if (parts.Length == 8)
        {
            if (!parts[4].Equals("seat", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(parts[5], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
                || !int.TryParse(parts[6], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)
                || !int.TryParse(parts[7], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z))
            {
                return FrameUsage;
            }

            seat = new CellPos(x, y, z);
        }

        if (!frames.TryReparent(new EntityId(id), frame, seat, out var applied, out var failure))
        {
            return string.Create(CultureInfo.InvariantCulture, $"FRAME refused entity={id} reason={failure}");
        }

        var op = (EntityReparentOp)applied!.Operation;
        return string.Create(CultureInfo.InvariantCulture, $"FRAME reparent entity={id} parent={FormatFrame(op.NewParent)} seq={op.NewParentSeq} op={applied.Id.Value}");
    }

    // Fixed-format FRAME lines (K5-20 step 7): "frame list" and "frame reparent <entity> <frame> [seat x y z]".
    private string Frame(string[] parts)
    {
        if (parts.Length < 2)
        {
            return FrameUsage;
        }

        if (_runtime.RuntimeState is not (ServerRuntimeState.Running or ServerRuntimeState.SafeMode) || _frames!() is not { } frames)
        {
            return "the server is not running";
        }

        return parts[1].ToLowerInvariant() switch
        {
            "list" when parts.Length == 2 => FrameList(frames.Graph),
            "reparent" when parts.Length is 4 or 8 => FrameReparent(frames, parts),
            _ => FrameUsage,
        };
    }

    private string FrameList(FrameGraph graph)
    {
        var lines = new List<string>();
        foreach (var construct in _runtime.State.Constructs.All)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"FRAME construct {construct.Id.Value} parent={FormatFrame(construct.Parent)} seq={construct.ParentSeq} mode={construct.Mode} world={FormatWorld(graph, FrameRef.Of(construct.Id), PoseD.Identity)}"));
        }

        foreach (var player in _runtime.State.Players.Online)
        {
            var seat = player.Seat is { } cell ? string.Create(CultureInfo.InvariantCulture, $"{cell.X},{cell.Y},{cell.Z}") : "-";
            var local = FrameMath.FromFloat(player.Position, player.BodyRotation);
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"FRAME player {player.Player.Value} entity={player.Entity.Value} parent={FormatFrame(player.Frame)} seq={player.ParentSeq} seat={seat} world={FormatWorld(graph, player.Frame, local)}"));
        }

        return lines.Count == 0 ? "FRAME none" : string.Join('\n', lines);
    }
}
