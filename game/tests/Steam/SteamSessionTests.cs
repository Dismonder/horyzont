using Godot;

using GdUnit4;

using Horyzont.Client.Steam;
using Horyzont.Game.Boot;
using Horyzont.Core.Ids;
using Horyzont.Game.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Steam;

[TestSuite]
public class SteamSessionTests
{
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Host_LocalClient_UsesDevIdentityWithSteamEnabled()
    {
        await using var scene = EngineTestScene.Create();
        var time = new FakeTimeProvider();
        var dir = ModeFixture.NewDirectory();
        var api = new FakeSteamLobbyApi { InitializeResult = false };
        var services = SteamPlatformServices.Create(api, 480, "1", 3, time, NullLoggerFactory.Instance);
        var composition = CreateHost(dir, time, services);
        scene.Add(composition.Root);

        AssertThat(await composition.StartAsync(default)).IsNull();
        AssertThat(await scene.WaitFramesUntil(() => composition.Client!.Hello is not null, 10000)).IsTrue();

        // Steam enabled but unavailable: the host stays on LAN, no lobby, and the identity the server registered is the Dev profile one.
        AssertThat(api.CreateRequests.Count).IsEqual(0);
        AssertThat(api.JoinRequests.Count).IsEqual(0);
        AssertThat(composition.Server!.State.Players.TryGet(composition.Client!.Hello!.YourPlayer, out var player)).IsTrue();
        var registered = player!.Identity;
        AssertThat(registered.Platform).IsEqual(IdentityPlatform.Dev);
        AssertThat(registered).IsEqual(LocalProfile.LoadOrCreate(Path.Combine(dir, "profile.json")).Identity);
        composition.RequestShutdown();
        AssertThat(await scene.WaitFramesUntil(() => composition.IsFinished, 10000)).IsTrue();
        AssertThat(await scene.WaitFramesUntil(composition.ReleaseStep, 600)).IsTrue();
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task SteamLifetime_MultipleSessions_InitializesAndPumpsOncePerFrame()
    {
        await using var scene = EngineTestScene.Create();
        var time = new FakeTimeProvider();
        var api = new FakeSteamLobbyApi { InitializeResult = false };
        var services = SteamPlatformServices.Create(api, 480, "1", 3, time, NullLoggerFactory.Instance);
        var lifetime = new SteamLifetime();
        lifetime.Initialize(services);
        scene.Add(lifetime);

        var pumpsAtStart = api.PumpCount;
        var framesAtStart = (long)Engine.GetProcessFrames();
        for (var session = 0; session < 2; session++)
        {
            var composition = CreateHost(ModeFixture.NewDirectory(), time, services);
            scene.Add(composition.Root);
            AssertThat(await composition.StartAsync(default)).IsNull();
            composition.RequestShutdown();
            AssertThat(await scene.WaitFramesUntil(() => composition.IsFinished, 10000)).IsTrue();
            AssertThat(await scene.WaitFramesUntil(composition.ReleaseStep, 600)).IsTrue();
        }

        // One SDK init for the process, and exactly one pump per process frame across both sessions (allowing the frame boundary).
        var pumps = api.PumpCount - pumpsAtStart;
        var frames = (long)Engine.GetProcessFrames() - framesAtStart;
        AssertThat(api.InitCount).IsEqual(1);
        AssertThat(Math.Abs(pumps - frames) <= 1).IsTrue();
        AssertThat(api.ShutdownCount).IsEqual(0);
    }

    private static GameComposition CreateHost(string dir, TimeProvider time, SteamPlatformServices services)
    {
        var env = new GameEnvironment
        {
            Time = time, Loggers = NullLoggerFactory.Instance, UserDataDir = dir, ExecutableDir = dir,
            Characters = false, Print = _ => { }, Platform = services,
        };
        return GameComposition.Create(new LaunchOptions
        {
            Mode = Horyzont.Server.ProcessMode.Host, DataDir = dir, BindAddress = "127.0.0.1", Port = ModeFixture.NextPort(), EnableSteam = true,
        }, env);
    }
}
