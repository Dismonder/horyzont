using GdUnit4;

using Horyzont.Client.Steam;
using Horyzont.Game.Boot;
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
        var env = new GameEnvironment
        {
            Time = time, Loggers = NullLoggerFactory.Instance, UserDataDir = dir, ExecutableDir = dir,
            Characters = false, Print = _ => { }, Platform = services,
        };
        var composition = GameComposition.Create(new LaunchOptions
        {
            Mode = Horyzont.Server.ProcessMode.Host, DataDir = dir, BindAddress = "127.0.0.1", Port = ModeFixture.NextPort(), EnableSteam = true,
        }, env);
        scene.Add(composition.Root);

        AssertThat(await composition.StartAsync(default)).IsNull();
        AssertThat(await scene.WaitFramesUntil(() => composition.Client!.Hello is not null, 10000)).IsTrue();

        // Steam enabled but unavailable: the host stays on LAN, no lobby, and the local client keeps its Dev profile identity.
        AssertThat(api.CreateRequests.Count).IsEqual(0);
        AssertThat(api.JoinRequests.Count).IsEqual(0);
        AssertThat(LocalProfile.LoadOrCreate(Path.Combine(dir, "profile.json")).Identity.Platform).IsEqual(Horyzont.Core.Ids.IdentityPlatform.Dev);
        composition.RequestShutdown();
        AssertThat(await scene.WaitFramesUntil(() => composition.IsFinished, 10000)).IsTrue();
        AssertThat(await scene.WaitFramesUntil(composition.ReleaseStep, 600)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task SteamLifetime_MultipleSessions_InitializesAndPumpsOncePerFrame()
    {
        await using var scene = EngineTestScene.Create();
        var api = new FakeSteamLobbyApi();
        var services = SteamPlatformServices.Create(api, 480, "1", 3, new FakeTimeProvider(), NullLoggerFactory.Instance);
        var lifetime = new SteamLifetime();
        lifetime.Initialize(services);
        scene.Add(lifetime);

        AssertThat(await scene.WaitFramesUntil(() => lifetime.Pumped >= 5, 100)).IsTrue();

        AssertThat(api.InitCount).IsEqual(1);
        AssertThat(api.PumpCount).IsEqual(lifetime.Pumped);
        AssertThat(api.ShutdownCount).IsEqual(0);
    }
}
