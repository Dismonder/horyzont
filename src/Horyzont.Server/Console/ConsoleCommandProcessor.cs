using System.Globalization;
using System.Text;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Console;

/// <summary>Executes the commands of the server console (save, stop, players, kick, help) on the main thread.</summary>
public sealed class ConsoleCommandProcessor
{
    /// <summary>Longest accepted input line.</summary>
    public const int MaxLineLength = 256;

    private const string Help =
        "commands:\n  save            write a checkpoint now (also leaves safe mode after a write error)\n  stop            save and shut the server down\n  players         list the players in game\n  kick <id>       disconnect a player by player id\n  help            this list";

    private readonly ServerRuntime _runtime;
    private readonly ILogger<ConsoleCommandProcessor> _logger;

    /// <summary>Initializes a new instance of the <see cref="ConsoleCommandProcessor"/> class.</summary>
    /// <param name="runtime">The server the commands act on.</param>
    /// <param name="logger">Logger.</param>
    public ConsoleCommandProcessor(ServerRuntime runtime, ILogger<ConsoleCommandProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(logger);
        _runtime = runtime;
        _logger = logger;
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
}
