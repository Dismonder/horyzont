using System.Globalization;
using System.Net;

using Horyzont.Bots.Scripting;

namespace Horyzont.Bots.Runner;

/// <summary>The command line of <c>tests/Horyzont.BotRunner</c>.</summary>
public sealed record RunnerOptions
{
    private static readonly BotRole[] _defaultRoles =
    [
        BotRole.Builder, BotRole.Builder, BotRole.Builder, BotRole.Builder, BotRole.Builder, BotRole.Explorer, BotRole.Explorer, BotRole.Abuser,
    ];

    /// <summary>Gets the server address (an IPv4 literal).</summary>
    public required string Host { get; init; }

    /// <summary>Gets the server UDP port.</summary>
    public required int Port { get; init; }

    /// <summary>Gets the number of bots.</summary>
    public int Bots { get; init; } = 8;

    /// <summary>Gets the roles of the bots, cycled when there are more bots than entries.</summary>
    public IReadOnlyList<BotRole> Roles { get; init; } = _defaultRoles;

    /// <summary>Gets the seed of the first bot; bot i uses seed + i.</summary>
    public ulong Seed { get; init; } = 1;

    /// <summary>Gets the moment of script tick 0 as Unix milliseconds; 0 means two seconds from now.</summary>
    public long EpochUnixMilliseconds { get; init; }

    /// <summary>Gets the one-way delay added to both directions in milliseconds (the round trip is twice that).</summary>
    public int LatencyMilliseconds { get; init; }

    /// <summary>Gets the jitter in milliseconds (plus or minus).</summary>
    public int JitterMilliseconds { get; init; }

    /// <summary>Gets the length of the run in minutes; 0 runs until a <c>stop</c> line on the standard input or Ctrl+C.</summary>
    public double Minutes { get; init; }

    /// <summary>Gets the directory for <c>bots.json</c>.</summary>
    public string OutDirectory { get; init; } = ".";

    /// <summary>Gets the generator profiles of the build hash (as <c>GeneratorProfiles.BuildHashComponent</c> of the game).</summary>
    public string GeneratorProfiles { get; init; } = "core:planet/1";

    /// <summary>Gets the first platform identity value of the bots (bot i uses value + i); stable, so a bot returns as the same player.</summary>
    public ulong IdentityBase { get; init; } = 7000;

    /// <summary>Gets the length of a control period in seconds (default 900): a check break with dumps ends every period.</summary>
    public int ControlSeconds { get; init; } = 900;

    /// <summary>Gets the quiet window before the end of a control period, in seconds (default 60).</summary>
    public int QuietSeconds { get; init; } = 60;

    /// <summary>Gets the drain window at the end of a control period, in seconds (default 20).</summary>
    public int DrainSeconds { get; init; } = 20;

    /// <summary>Gets the shared timetable of the bots.</summary>
    public BotSchedule Schedule => new()
    {
        ControlPeriodTicks = (long)ControlSeconds * BotSchedule.TicksPerSecond,
        QuietTicks = (long)QuietSeconds * BotSchedule.TicksPerSecond,
        DrainTicks = (long)DrainSeconds * BotSchedule.TicksPerSecond,
    };

    /// <summary>Returns the role of bot <paramref name="index"/>.</summary>
    /// <param name="index">Bot number from 0.</param>
    /// <returns>The role.</returns>
    public BotRole RoleOf(int index) => Roles[index % Roles.Count];

    /// <summary>Parses a command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The options.</returns>
    /// <exception cref="ArgumentException">The line is not valid; the message says why.</exception>
    public static RunnerOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count)
            {
                throw new ArgumentException($"Expected '--name value' at '{args[i]}'.");
            }

            if (!values.TryAdd(args[i], args[i + 1]))
            {
                throw new ArgumentException($"{args[i]} given twice.");
            }
        }

        string[] known =
        [
            "--connect", "--bots", "--roles", "--seed", "--epoch", "--latency-ms", "--jitter-ms", "--minutes", "--out", "--profiles", "--identity-base",
            "--control-seconds", "--quiet-seconds", "--drain-seconds",
        ];
        foreach (var key in values.Keys.Where(k => Array.IndexOf(known, k) < 0))
        {
            throw new ArgumentException($"Unknown option {key}.");
        }

        if (!values.TryGetValue("--connect", out var connect))
        {
            throw new ArgumentException("--connect <ipv4:port> is required.");
        }

        var colon = connect.LastIndexOf(':');
        if (colon <= 0 || !IPAddress.TryParse(connect[..colon], out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !int.TryParse(connect[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            throw new ArgumentException("--connect must be <ipv4 address>:<port>.");
        }

        var latency = Integer(values, "--latency-ms", 0, 0, 5000);
        var jitter = Integer(values, "--jitter-ms", 0, 0, 5000);
        if (jitter > latency)
        {
            throw new ArgumentException("--jitter-ms must not exceed --latency-ms.");
        }

        var control = Integer(values, "--control-seconds", 900, 30, 86_400);
        var quiet = Integer(values, "--quiet-seconds", 60, 0, 3600);
        var drain = Integer(values, "--drain-seconds", 20, 5, 3600);
        if (quiet + drain >= control)
        {
            throw new ArgumentException("--quiet-seconds plus --drain-seconds must be shorter than --control-seconds.");
        }

        var minutes = 0.0;
        if (values.TryGetValue("--minutes", out var minutesText)
            && (!double.TryParse(minutesText, NumberStyles.Float, CultureInfo.InvariantCulture, out minutes) || minutes < 0 || !double.IsFinite(minutes)))
        {
            throw new ArgumentException("--minutes must be a number >= 0.");
        }

        return new RunnerOptions
        {
            Host = address.ToString(),
            Port = port,
            Bots = Integer(values, "--bots", 8, 1, 64),
            Roles = values.TryGetValue("--roles", out var rolesText) ? ParseRoles(rolesText) : _defaultRoles,
            Seed = values.TryGetValue("--seed", out var seed) ? ParseUnsigned(seed, "--seed") : 1,
            EpochUnixMilliseconds = values.TryGetValue("--epoch", out var epoch) ? (long)ParseUnsigned(epoch, "--epoch") : 0,
            LatencyMilliseconds = latency,
            JitterMilliseconds = jitter,
            Minutes = minutes,
            OutDirectory = values.GetValueOrDefault("--out", "."),
            GeneratorProfiles = values.GetValueOrDefault("--profiles", "core:planet/1"),
            IdentityBase = values.TryGetValue("--identity-base", out var identity) ? ParseUnsigned(identity, "--identity-base") : 7000,
            ControlSeconds = control,
            QuietSeconds = quiet,
            DrainSeconds = drain,
        };
    }

    private static int Integer(Dictionary<string, string> values, string key, int fallback, int min, int max)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            throw new ArgumentException($"{key} must be a whole number from {min} to {max}.");
        }

        return value;
    }

    private static ulong ParseUnsigned(string text, string key) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"{key} must be a non-negative whole number.");

    private static List<BotRole> ParseRoles(string text)
    {
        // "builder:5,explorer:2,abuser:1"
        var roles = new List<BotRole>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':');
            if (pieces.Length > 2 || !Enum.TryParse<BotRole>(pieces[0], ignoreCase: true, out var role) || !Enum.IsDefined(role))
            {
                throw new ArgumentException($"--roles: '{part}' is not <role>[:count] with a role of builder, explorer or abuser.");
            }

            var count = 1;
            if (pieces.Length == 2 && (!int.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out count) || count is < 1 or > 64))
            {
                throw new ArgumentException($"--roles: the count in '{part}' must be from 1 to 64.");
            }

            roles.AddRange(Enumerable.Repeat(role, count));
        }

        return roles.Count == 0 ? throw new ArgumentException("--roles is empty.") : roles;
    }
}
