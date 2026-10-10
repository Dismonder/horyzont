using System.Numerics;

using BenchmarkDotNet.Attributes;

using Horyzont.Bots.Client;
using Horyzont.Bots.Scripting;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.World;
using Horyzont.Integration.Tests.Coop;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Persistence;
using Horyzont.Server;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Benchmarks;

/// <summary>
/// One <see cref="ServerRuntime.Tick"/> with 8 bots in memory (input every tick, about one edit a second per player) over the fake terrain.
/// The setup runs 20 s of virtual time so the steady state is measured; the bots are stepped outside the measured call.
/// </summary>
[MemoryDiagnoser]
public class ServerTickBench
{
    private const ulong Build = 0x4B34_0000_0000_0019UL;

    private static readonly TimeSpan _frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private readonly List<BotClient> _bots = [];
    private FakeTimeProvider _time = null!;
    private InMemoryNetwork _network = null!;
    private InMemoryTransport _transport = null!;
    private FakeServerTerrainFactory _terrain = null!;
    private ServerRuntime _server = null!;
    private string _dir = null!;
    private long _frameIndex;
    private int _spawned;

    [GlobalSetup]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "horyzont-bench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        _network = new InMemoryNetwork(_time, null);
        _transport = _network.CreateTransport("server", TransportOptions.Default);
        _terrain = new FakeServerTerrainFactory(_time);
        var characters = new CoopCharacters();
        var options = new ServerRuntimeOptions
        {
            WorldPath = Path.Combine(_dir, "saves", "world.db"),
            BackupsDirectory = Path.Combine(_dir, "saves", "backups"),
            ServerOptions = new ServerOptions { BuildHash = Build },
            PersistenceOptions = new PersistenceOptions { GameVersion = "bench", BackupInterval = Timeout.InfiniteTimeSpan },
            Listen = [new InMemoryEndpoint("server")],
            BlockTypeKeys = [WorldFactory.StoneBlockKey],
            MaterialKeys = ["stone", "dirt"],
            NewPlayerSpawn = SpawnOnEquator,
        };
        _server = new ServerRuntime(ProcessMode.DedicatedServer, options, _transport, _terrain, WorldFactory.CreateDefault, _time, NullLoggerFactory.Instance);
        _server.AddClientMessageHandler(characters);
        _server.AddTickParticipant(characters);
        _server.AddPlayerObserver(characters);
        var result = _server.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (!result.IsRunning)
        {
            throw new InvalidOperationException("the server did not start: " + result);
        }

        characters.Bind(_server);
        for (var i = 0; i < 8; i++)
        {
            var role = i < 5 ? BotRole.Builder : i < 7 ? BotRole.Explorer : BotRole.Abuser;
            _bots.Add(new BotClient(
                new BotClientOptions
                {
                    Name = "bot" + i,
                    Identity = new PlatformIdentity(IdentityPlatform.Dev, 5000UL + (ulong)i),
                    Server = new InMemoryEndpoint("server"),
                    BuildHash = Build,
                    Seed = 100UL + (ulong)i,
                    Role = role,
                    Schedule = new BotSchedule(),
                    ReconnectDelay = TimeSpan.FromSeconds(2),
                },
                _network.CreateTransport("bot" + i, TransportOptions.Default), _time));
        }

        for (var i = 0; i < 20 * 60; i++)
        {
            Step();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var bot in _bots)
        {
            bot.Runtime.Session.Disconnect();
        }

        _transport.Dispose();
        foreach (var terrain in _terrain.Created)
        {
            terrain.Dispose();
        }
    }

    [IterationSetup]
    public void BeforeIteration()
    {
        // The bots produce this tick's input and intents outside the measured call.
        _time.Advance(_frame);
        foreach (var bot in _bots)
        {
            bot.Tick(_frameIndex);
        }

        _frameIndex++;
    }

    [Benchmark]
    public void Tick() => _server.Tick();

    private void Step()
    {
        _time.Advance(_frame);
        _server.Tick();
        foreach (var bot in _bots)
        {
            bot.Tick(_frameIndex);
        }

        _frameIndex++;
    }

    private (Vector3 Feet, Quaternion BodyRotation) SpawnOnEquator(PlanetParams planet)
    {
        var angle = _spawned++ * (MathF.PI / 4f);
        var feet = new Vector3(planet.RadiusMeters + 0.1f, 3f * MathF.Sin(angle), 3f * MathF.Cos(angle));
        var up = Vector3.Normalize(feet);
        return (up * (planet.RadiusMeters + 0.1f), SurfaceFrame.FromYaw(up, 0f));
    }
}
