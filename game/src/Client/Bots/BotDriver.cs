using Godot;

using Horyzont.Bots.Runner;
using Horyzont.Bots.Scripting;
using Horyzont.Client.Replication;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Client.Terrain;

namespace Horyzont.Game.Client.Bots;

/// <summary>
/// The node of the bot mode (<c>--bot</c>, K4-19), a child of the client: it owns the clock of the script (ticks since the shared
/// epoch, the timetable of the bots without an engine), writes the dump of every check break at the same moment as they do
/// (<see cref="BotCrowd.BreakAt"/>) and <c>engine-bot.json</c> every 30 s and when it leaves the tree.
/// </summary>
public sealed partial class BotDriver : Node
{
    private const double ReportEverySeconds = 30;

    private LocalPlayer? _player;
    private ClientReplication? _replication;
    private OwnTerrainProvider? _terrain;
    private ClientTerrainReplicator? _terrainReplicator;
    private BotInputSource? _input;
    private BotSchedule _schedule = new();
    private DateTimeOffset _epoch;
    private string _out = ".";
    private string _role = "builder";
    private long _lastTick = long.MinValue;
    private long _frames;
    private int _breaks;
    private double _sinceReport;

    /// <summary>Gets the input the script produces; null before <see cref="Initialize"/>.</summary>
    public BotInputSource? Input => _input;

    /// <summary>Gets the script tick now: ticks since the epoch at 60 per second, negative before it.</summary>
    public long ScriptTick
    {
        get
        {
            var elapsed = DateTimeOffset.UtcNow - _epoch;
            return (long)Math.Floor(elapsed.TotalSeconds * BotSchedule.TicksPerSecond);
        }
    }

    /// <summary>Wires the driver; call once before it enters the tree.</summary>
    /// <param name="seed">Seed of the script.</param>
    /// <param name="role">Role of the bot.</param>
    /// <param name="schedule">The shared timetable.</param>
    /// <param name="epoch">The shared epoch.</param>
    /// <param name="outDirectory">Where the report and the dumps go.</param>
    /// <param name="player">The local player to drive.</param>
    /// <param name="replication">The client replication (construct mirror); null when there is none.</param>
    /// <param name="terrain">The own terrain of the client; null when there is none.</param>
    /// <param name="terrainReplicator">The terrain replication of the client; null when there is none.</param>
    public void Initialize(
        ulong seed, BotRole role, BotSchedule schedule, DateTimeOffset epoch, string outDirectory, LocalPlayer player,
        ClientReplication? replication, OwnTerrainProvider? terrain, ClientTerrainReplicator? terrainReplicator)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(player);
        _schedule = schedule;
        _epoch = epoch;
        _out = outDirectory;
        _role = role.ToString().ToLowerInvariant();
        _player = player;
        _replication = replication;
        _terrain = terrain;
        _terrainReplicator = terrainReplicator;
        _input = new BotInputSource(new BotScript(seed, role, schedule), player, () => ScriptTick);
        Name = "BotDriver";
        Directory.CreateDirectory(_out);
    }

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        if (_player is null)
        {
            return;
        }

        var tick = ScriptTick;
        if (_player.IsActive)
        {
            _frames++;
        }

        // Every tick between the last frame and this one is checked, so a slow frame does not skip a break.
        for (var t = Math.Max(_lastTick + 1, tick - 30); t <= tick; t++)
        {
            if (BotCrowd.BreakAt(t, _schedule) is var index and > 0)
            {
                EngineBotReport.WriteBreak(Path.Combine(_out, "dumps"), index, BotName, writer =>
                    EngineBotReport.WriteDump(writer, BotName, _role, _player, _replication?.Constructs, _terrain));
                _breaks++;
            }
        }

        _lastTick = tick;
        _sinceReport += delta;
        if (_sinceReport >= ReportEverySeconds)
        {
            _sinceReport = 0;
            WriteReport();
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_player is not null)
        {
            WriteReport();
        }
    }

    private string BotName => "engine-" + Path.GetFileName(Path.TrimEndingDirectorySeparator(_out));

    private void WriteReport() =>
        EngineBotReport.Write(
            Path.Combine(_out, "engine-bot.json"), BotName, _player!, _terrainReplicator?.Statistics, _frames, _input?.ToolActions ?? 0, _breaks);
}
