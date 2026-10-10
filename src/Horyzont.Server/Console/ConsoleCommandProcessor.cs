using System.Globalization;
using System.Text;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Frames;

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
