using System.Globalization;

using Horyzont.Bots.Client;
using Horyzont.Bots.Replicas;
using Horyzont.Bots.Runner;
using Horyzont.Bots.Transport;
using Horyzont.Core;
using Horyzont.Core.Ids;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.Udp;

namespace Horyzont.BotRunner;

/// <summary>
/// N bots without an engine in one process, over real UDP and real time (K4-19). They walk the path of a player and write
/// <c>bots.json</c> into the output directory. Lines start with <c>RUNNER</c> so the orchestrator can parse them. Stops at the end of
/// <c>--minutes</c>, on a <c>stop</c> line on the standard input or on Ctrl+C. Exit code 0; 2 for a bad command line; 3 when no bot
/// ever reached the game.
/// </summary>
internal static class Program
{
    private static readonly TimeSpan _reportEvery = TimeSpan.FromSeconds(30);

    private static int Main(string[] args)
    {
        RunnerOptions options;
        try
        {
            options = RunnerOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("RUNNER usage error: " + ex.Message);
            Console.Error.WriteLine("usage: BotRunner --connect <ipv4:port> [--bots 8] [--roles builder:5,explorer:2,abuser:1] [--seed 1] [--epoch <unix ms>] [--latency-ms 50] [--jitter-ms 10] [--minutes 0] [--out dir] [--profiles core:planet/1] [--identity-base 7000] [--control-seconds 900] [--quiet-seconds 60] [--drain-seconds 20]");
            return 2;
        }

        Directory.CreateDirectory(options.OutDirectory);
        var stop = 0;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Volatile.Write(ref stop, 1);
        };
        _ = Task.Run(() =>
        {
            string? line;
            while ((line = Console.In.ReadLine()) is not null)
            {
                if (string.Equals(line.Trim(), "stop", StringComparison.OrdinalIgnoreCase))
                {
                    Volatile.Write(ref stop, 1);
                    return;
                }
            }
        });

        var time = TimeProvider.System;
        var epoch = options.EpochUnixMilliseconds == 0
            ? time.GetUtcNow().AddSeconds(2)
            : DateTimeOffset.FromUnixTimeMilliseconds(options.EpochUnixMilliseconds);
        var buildHash = BuildInfo.ComputeBuildHash(options.GeneratorProfiles);
        var server = new UdpEndpoint(options.Host, options.Port);
        var bots = new List<BotClient>();
        var transports = new List<ITransport>();
        for (var i = 0; i < options.Bots; i++)
        {
            ITransport transport = new UdpTransport(TransportOptions.Default);
            if (options.LatencyMilliseconds > 0)
            {
                transport = new LatencyTransport(
                    transport, time, TimeSpan.FromMilliseconds(options.LatencyMilliseconds), TimeSpan.FromMilliseconds(options.JitterMilliseconds),
                    seed: (int)(options.Seed + (ulong)i));
            }

            transports.Add(transport);
            bots.Add(new BotClient(
                new BotClientOptions
                {
                    Name = "bot" + i.ToString(CultureInfo.InvariantCulture),
                    Identity = new PlatformIdentity(IdentityPlatform.Dev, options.IdentityBase + (ulong)i),
                    Server = server,
                    BuildHash = buildHash,
                    Seed = options.Seed + (ulong)i,
                    Role = options.RoleOf(i),
                    Schedule = options.Schedule,
                },
                transport, time,
                new BotReplicaSet(
                    new ConstructMirrorReplica(time),
                    new TerrainReplica(Path.Combine(options.OutDirectory, "cache", "bot" + i.ToString(CultureInfo.InvariantCulture)), time))));
        }

        var latency = new EditLatencyTracker(time);
        for (var i = 0; i < bots.Count; i++)
        {
            latency.Attach(bots[i], i);
        }

        var crowd = new BotCrowd(bots, time, epoch)
        {
            Latency = latency,
            DumpDirectory = Path.Combine(options.OutDirectory, "dumps"),
            Schedule = options.Schedule,
        };
        var reportPath = Path.Combine(options.OutDirectory, "bots.json");
        var started = time.GetTimestamp();
        var end = options.Minutes > 0 ? time.GetUtcNow().AddSeconds(2 + (options.Minutes * 60)) : DateTimeOffset.MaxValue;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"RUNNER READY bots={options.Bots} epoch_ms={epoch.ToUnixTimeMilliseconds()} server={options.Host}:{options.Port}"));
        var nextReport = time.GetTimestamp() + (long)(_reportEvery.TotalSeconds * time.TimestampFrequency);
        var everInGame = false;
        while (Volatile.Read(ref stop) == 0 && time.GetUtcNow() < end)
        {
            crowd.Pump();
            everInGame |= bots.Any(b => b.IsInGame);
            if (time.GetTimestamp() >= nextReport)
            {
                nextReport += (long)(_reportEvery.TotalSeconds * time.TimestampFrequency);
                BotReport.Write(reportPath, bots, time, crowd.LastFrame + 1, crowd.SkippedFrames, latency, crowd.Breaks);
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"RUNNER in_game={bots.Count(b => b.IsInGame)}/{bots.Count} frame={crowd.LastFrame} skipped={crowd.SkippedFrames} accepted={bots.Sum(b => b.Stats.Accepted)} rejected={bots.Sum(b => b.Stats.Rejected)} disconnects={bots.Sum(b => b.Stats.Disconnects)}"));
            }

            Thread.Sleep(1);
        }

        foreach (var bot in bots)
        {
            bot.Leave();
        }

        // Let the disconnects go out.
        var drainUntil = time.GetTimestamp() + (time.TimestampFrequency / 5);
        while (time.GetTimestamp() < drainUntil)
        {
            foreach (var bot in bots)
            {
                bot.Runtime.Poll();
            }

            Thread.Sleep(5);
        }

        BotReport.Write(reportPath, bots, time, crowd.LastFrame + 1, crowd.SkippedFrames, latency, crowd.Breaks);
        foreach (var bot in bots)
        {
            (bot.Replica as IDisposable)?.Dispose();
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"RUNNER DONE seconds={time.GetElapsedTime(started).TotalSeconds:0.0} accepted={bots.Sum(b => b.Stats.Accepted)} rejected={bots.Sum(b => b.Stats.Rejected)} report={reportPath}"));
        foreach (var transport in transports)
        {
            transport.Dispose();
        }

        return everInGame ? 0 : 3;
    }
}
