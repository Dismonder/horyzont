using GdUnit4;

using Horyzont.Client.Platform;
using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Game.Boot;
using Horyzont.Game.Tests.Support;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Boot;

[TestSuite]
public class PlatformSeamTests
{
    [TestCase]
    [RequireGodotRuntime]
    public async Task Create_RemotePlatformWithoutConnect_ChoosesTransportAndDeferredConnection()
    {
        await using var scene = EngineTestScene.Create();
        var time = new FakeTimeProvider();
        var network = new InMemoryNetwork(time);
        var transport = network.CreateTransport("client", TransportOptions.Default);
        var platform = new Platform(transport);
        var dir = ModeFixture.NewDirectory();
        var composition = GameComposition.Create(new LaunchOptions { Mode = Horyzont.Server.ProcessMode.Client }, Environment(dir, time, platform));
        scene.Add(composition.Root);
        AssertThat(platform.Calls[0]).IsEqual("transport");
        AssertThat(await composition.StartAsync(default)).IsNull();
        AssertThat(platform.Calls[1]).IsEqual("connect");
        AssertThat(composition.Client!.Session.State).IsEqual(ClientState.Disconnected);
        composition.RequestShutdown();
        AssertThat(await scene.WaitFramesUntil(composition.ReleaseStep, 600)).IsTrue();
        AssertThat(string.Join(",", platform.Calls)).IsEqual("transport,connect,shutdown,released");
        AssertThat(composition.ReleaseStep()).IsTrue();
        AssertThat(platform.Calls.Count).IsEqual(4);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Host_StartedShutdownAndRelease_NotifiesInOrderAndCleansProcessLast()
    {
        await using var scene = EngineTestScene.Create();
        var time = new FakeTimeProvider();
        var dir = ModeFixture.NewDirectory();
        var platform = new Platform(null);
        var processCalls = 0;
        var env = Environment(dir, time, platform) with
        {
            ManageQuit = true,
            ProcessCleanup = () => { processCalls++; platform.Calls.Add("process"); return true; },
        };
        var composition = GameComposition.Create(new LaunchOptions
        {
            Mode = Horyzont.Server.ProcessMode.Host, DataDir = dir, BindAddress = "127.0.0.1", Port = ModeFixture.NextPort(),
        }, env);
        scene.Add(composition.Root);
        composition.Quit!.SetProcess(false);
        AssertThat(platform.Calls[0]).IsEqual("server-transports");
        AssertThat(await composition.StartAsync(default, connectClient: false)).IsNull();
        AssertThat(platform.Calls[1]).IsEqual("world:8");
        composition.RequestShutdown();
        composition.RequestShutdown();
        AssertThat(processCalls).IsEqual(0);
        AssertThat(await scene.WaitFramesUntil(() => composition.IsFinished, 10000)).IsTrue();
        AssertThat(await scene.WaitFramesUntil(composition.Quit.Advance, 10000)).IsTrue();
        AssertThat(string.Join(",", platform.Calls)).IsEqual("server-transports,world:8,shutdown,released,process");
        AssertThat(processCalls).IsEqual(1);
    }

    private static GameEnvironment Environment(string dir, TimeProvider time, ICompositionPlatform platform) => new()
    {
        Time = time, Loggers = NullLoggerFactory.Instance, UserDataDir = dir, ExecutableDir = dir,
        Characters = false, Print = _ => { }, CompositionPlatform = platform,
    };

    private sealed class Platform(ITransport? transport) : ICompositionPlatform
    {
        public List<string> Calls { get; } = [];
        public void ConfigureServerTransports(IList<ITransport> transports, IList<TransportEndpoint> listen)
        {
            // Runs before ServerRuntime owns the closed composite; both K2 host listeners remain available.
            AssertThat(transports.Count).IsEqual(2);
            AssertThat(listen.Count).IsEqual(2);
            Calls.Add("server-transports");
        }
        public ITransport CreateClientTransport(Func<ITransport> fallback)
        {
            Calls.Add("transport");
            return transport!;
        }
        public bool TryConnectClient(ClientRuntime client) { Calls.Add("connect"); return true; }
        public void OnWorldStarted(WorldId world, int capacity, bool passwordRequired) => Calls.Add($"world:{capacity}");
        public void OnShutdownRequested() => Calls.Add("shutdown");
        public void OnTransportsReleased() => Calls.Add("released");
    }
}
