using System.Diagnostics;
using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Persistence;
using Horyzont.Testing;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Support;

/// <summary>
/// A server process in miniature: a <see cref="ServerRuntime"/> over an in-memory network, the fake terrain, a world file in a
/// temporary directory and clients that are driven frame by frame (server tick first, then the clients, like the host).
/// </summary>
internal sealed class ServerRig : IAsyncDisposable
{
    public const ulong Build = 0x4B32_0000_0000_0010UL;

    public static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(16);

    private readonly List<RigClient> _clients = [];
    private readonly bool _ownsDirectory;

    public ServerRig(
        TempDirectory? dir = null, FakeTimeProvider? time = null, Func<ServerRuntimeOptions, ServerRuntimeOptions>? configure = null,
        ProcessMode mode = ProcessMode.Solo, TestFaults? faults = null, FakeServerTerrainFactory? terrain = null,
        Func<WorldState>? createWorld = null, LinkConditions? conditions = null)
    {
        _ownsDirectory = dir is null;
        Dir = dir ?? new TempDirectory();
        Time = time ?? new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        Terrain = terrain ?? new FakeServerTerrainFactory(Time);
        Network = new InMemoryNetwork(Time, conditions);
        ServerTransport = Network.CreateTransport("server", TransportOptions.Default);
        Faults = faults ?? new TestFaults();
        var options = new ServerRuntimeOptions
        {
            WorldPath = Dir.WorldPath,
            ServerOptions = new ServerOptions { BuildHash = Build },
            PersistenceOptions = new PersistenceOptions { GameVersion = "k2-10-test", BackupInterval = Timeout.InfiniteTimeSpan },
            Listen = [new InMemoryEndpoint("server")],
            LocalClientEndpoint = new InMemoryEndpoint("local"),
            Faults = Faults,
        };
        Options = configure?.Invoke(options) ?? options;
        Runtime = new ServerRuntime(mode, Options, ServerTransport, Terrain, createWorld ?? WorldFactory.CreateDefault, Time, Log);
    }

    public TempDirectory Dir { get; }

    public FakeTimeProvider Time { get; }

    public RecordingLoggerFactory Log { get; } = new();

    public FakeServerTerrainFactory Terrain { get; }

    public InMemoryNetwork Network { get; }

    public InMemoryTransport ServerTransport { get; }

    public TestFaults Faults { get; }

    public ServerRuntimeOptions Options { get; }

    public ServerRuntime Runtime { get; }

    public IReadOnlyList<RigClient> Clients => _clients;

    public ulong Hash => StateHasher.Hash(Runtime.State);

    public Task<WorldStartResult> StartAsync() => Runtime.StartAsync(TestContext.Current.CancellationToken);

    public RigClient AddClient(string name, ulong identity)
    {
        var client = new RigClient(Network, name, new PlatformIdentity(IdentityPlatform.Dev, identity), Build, Time);
        _clients.Add(client);
        client.Session.Connect(new InMemoryEndpoint("server"), client.Identity, name);
        return client;
    }

    /// <summary>Advances the clock by one frame: the server ticks first, then every client polls.</summary>
    public void Step()
    {
        Time.Advance(FrameTime);
        Runtime.Tick();
        foreach (var client in _clients)
        {
            client.Session.Poll();
        }
    }

    /// <summary>Steps a number of frames without yielding (rate limits refill per tick).</summary>
    public void StepMany(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            Step();
        }
    }

    /// <summary>Steps until the condition holds; yields to the writer thread between frames. Bounded by frames and by real time.</summary>
    public async Task<bool> RunUntilAsync(Func<bool> condition, int maxFrames = 100_000)
    {
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < maxFrames && watch.Elapsed < TimeSpan.FromSeconds(30); i++)
        {
            if (condition())
            {
                return true;
            }

            Step();
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        return condition();
    }

    public async Task<RigClient> JoinAsync(string name, ulong identity)
    {
        var client = AddClient(name, identity);
        Assert.True(await RunUntilAsync(() => client.Session.State == ClientState.Loading), "client did not reach Loading");
        client.Session.SendReady();
        Assert.True(await RunUntilAsync(() => client.Session.State == ClientState.InGame), "client did not reach InGame");
        return client;
    }

    /// <summary>Sends a terrain dig at ground point (x, z) as the client and waits for the reply.</summary>
    public async Task<IntentReply> DigAsync(RigClient client, float x, float z)
    {
        var replies = client.Replies.Count;
        client.Session.SubmitIntent(DigIntent(client.Player, x, z));
        Assert.True(await RunUntilAsync(() => client.Replies.Count > replies), "no intent reply");
        return client.Replies[^1];
    }

    public TerrainToolIntent DigIntent(PlayerId player, float x, float z)
    {
        Runtime.State.Players.TryGet(player, out var p);
        var eye = p!.EyePosition(Runtime.Simulation.Rules);
        var r = Terrain.Last.Terrain.SurfaceRadius;
        var ground = new Vector3(x, MathF.Sqrt((r * r) - (x * x) - (z * z)), z);
        return new TerrainToolIntent(WorldFactory.HomePlanet, eye, Vector3.Normalize(ground - eye), 1f, TerrainEditMode.Remove, 0);
    }

    /// <summary>Steps frames until the runtime has stopped.</summary>
    public async Task StopAsync()
    {
        Runtime.RequestShutdown();
        Assert.True(await RunUntilAsync(() => Runtime.IsStopped), "the server did not stop; steps done: " + string.Join(", ", Runtime.CompletedShutdownSteps));
    }

    public ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Transport.Dispose();
        }

        ServerTransport.Dispose();
        foreach (var terrain in Terrain.Created)
        {
            terrain.Dispose();
        }

        if (_ownsDirectory)
        {
            Dir.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
