using System.Diagnostics;

using Godot;

using Horyzont.Client.Session;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Game.Boot;
using Horyzont.Game.Terrain;
using Horyzont.Net.Session;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Game.Tests.Support;

/// <summary>
/// One <see cref="GameComposition"/> of a mode in the engine test scene, with a temporary data directory: the same code path as the
/// command line (<see cref="LaunchOptions.Parse"/> and <see cref="GameComposition.Create"/>). Waits use real time with a limit
/// because the physics ticks of the nodes come from the engine clock.
/// </summary>
internal sealed class ModeFixture
{
    private static int _nextPort = 41000 + (System.Environment.ProcessId % 2000);

    private readonly EngineTestScene _scene;

    private ModeFixture(EngineTestScene scene, GameComposition composition, string directory, List<string> printed)
    {
        Printed = printed;
        _scene = scene;
        Composition = composition;
        Directory = directory;
    }

    public GameComposition Composition { get; }

    public string Directory { get; }

    public List<string> Printed { get; }

    public int? StartExit { get; private set; }

    public static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "horyzont-k2-10-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A UDP port for a test, different on every call.</summary>
    public static int NextPort() => Interlocked.Increment(ref _nextPort);

    /// <summary>Parses the arguments like the command line, composes the mode, adds it to the scene and starts it.</summary>
    public static async Task<ModeFixture> StartAsync(EngineTestScene scene, string directory, bool connectClient, params string[] args)
    {
        var launch = LaunchOptions.Parse(args);
        var printed = new List<string>();
        var env = new GameEnvironment
        {
            Time = TimeProvider.System,
            Loggers = NullLoggerFactory.Instance,
            UserDataDir = Path.Combine(directory, "user"),
            ExecutableDir = directory,
            Render = false,
            Print = printed.Add,
        };
        var composition = GameComposition.Create(launch, env);
        var fixture = new ModeFixture(scene, composition, directory, printed);
        scene.Add(composition.Root);
        fixture.StartExit = await composition.StartAsync(CancellationToken.None, connectClient);
        return fixture;
    }

    /// <summary>Waits frame by frame until the condition holds or the real time limit passes.</summary>
    public async Task<bool> WaitUntil(Func<bool> condition, double seconds = 60)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > seconds)
            {
                return false;
            }

            await _scene.WaitFrames(1);
        }

        return true;
    }

    public Task<bool> WaitInGame(double seconds = 60) =>
        WaitUntil(() => Composition.Client?.Session.State == ClientState.InGame, seconds);

    /// <summary>The canonical shutdown as the quit coordinator starts it; returns when the process could exit.</summary>
    public async Task<bool> StopAsync(double seconds = 90)
    {
        Composition.RequestShutdown();
        var finished = await WaitUntil(() => Composition.IsFinished, seconds);
        Composition.Release();
        if (GodotObject.IsInstanceValid(Composition.Root))
        {
            Composition.Root.GetParent()?.RemoveChild(Composition.Root);
            Composition.Root.QueueFree();
        }

        await _scene.WaitFrames(2);
        return finished;
    }

    /// <summary>Y of the surface above the planet's north pole (+Y axis) from the generator.</summary>
    public static int SurfaceY(PlanetParams planet)
    {
        using var probe = new Horyzont.Game.Planet.Diagnostics.GeneratorProbe(Horyzont.Game.Planet.Generators.GeneratorProfiles.CreateGenerator(planet));
        for (var y = (int)planet.RadiusMeters + 400; y > (int)planet.RadiusMeters - 400; y--)
        {
            if (probe.Sample(0, y, 0).Sdf < 0f)
            {
                return y;
            }
        }

        throw new InvalidOperationException("No surface on +Y.");
    }

    /// <summary>Digest of every edited block as Voxel Tools holds it now (all of them must be loaded).</summary>
    public static ulong LiveDigest(VoxelTerrainService service, WorldState state)
    {
        var codec = new TerrainBlockCodec();
        var entries = new List<(Horyzont.Core.World.Terrain.TerrainBlockKey, uint, ulong)>();
        using var buffer = new VoxelBuffer();
        foreach (var version in state.TerrainVersions.All)
        {
            if (!service.TryCopyBlock(version.Key, buffer))
            {
                throw new InvalidOperationException($"Block {version.Key} is not loaded.");
            }

            entries.Add((version.Key, version.Version, codec.Digest(buffer)));
        }

        return TerrainDigest.Compute(entries);
    }

    public static string SimpleConfig(int port, Guid? world = null) =>
        $"{{ \"bindAddress\": \"127.0.0.1\", \"port\": {port}, \"worldId\": {(world is { } id ? $"\"{id}\"" : "null")} }}";

    /// <summary>Collects the replies of the server.</summary>
    internal sealed class Replies : IClientSessionListener
    {
        public List<IntentReply> All { get; } = [];

        public void OnIntentReply(IntentReply reply) => All.Add(reply);
    }

}
