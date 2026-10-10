using GdUnit4;

using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Game.Client.Sky;
using Horyzont.Game.Planet.Sky;
using Horyzont.Game.Tests.Support;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Logging.Abstractions;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class DayNightTests
{
    private static float Deviation(ClientDayNight sky, Horyzont.Core.World.PlanetParams planet, int tickRate)
    {
        var expected = DayNightCycle.SunDirection(sky.AppliedTick, tickRate, planet);
        var z = sky.Sun.GlobalBasis.Z;
        return (new System.Numerics.Vector3((float)z.X, (float)z.Y, (float)z.Z) - expected).Length();
    }

    /// <summary>In solo the sun follows the tick of the server of the same process (the light points along minus Z, so its Z axis is the direction to the sun).</summary>
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task DayNight_SunFollowsServerTick()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await ModeFixture.StartWithCharactersAsync(
            scene, dir, true, false, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        var composition = fixture.Composition;
        AssertThat(await fixture.WaitInGame(120)).IsTrue();
        var sky = composition.DayNight!;
        var hello = composition.Client!.Hello!;
        var planet = hello.Planets[0];
        AssertThat(await fixture.WaitUntil(() => sky.AppliedTick > 0, 30)).IsTrue();

        AssertThat(Deviation(sky, planet, hello.TickRate)).IsLess(1e-4f);
        var first = sky.Sun.GlobalBasis.Z;
        var applied = sky.AppliedTick;
        AssertThat(await fixture.WaitUntil(() => sky.AppliedTick >= applied + 600, 60)).IsTrue();
        AssertThat(Deviation(sky, planet, hello.TickRate)).IsLess(1e-4f);
        AssertThat(sky.Sun.GlobalBasis.Z.DistanceTo(first)).IsGreater(1e-6f);
        AssertThat(sky.AppliedTick).IsLessEqual(composition.Server!.State.CurrentTick.Value);

        AssertThat(await fixture.StopAsync()).IsTrue();
    }

    /// <summary>A remote client has no server tick of its own: it starts from the tick in the hello and counts on.</summary>
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task DayNight_RemoteClient_StartsFromTheTickOfTheHello()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await ModeFixture.StartWithCharactersAsync(
            scene, dir, true, false, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());
        AssertThat(await fixture.WaitInGame(120)).IsTrue();
        var real = fixture.Composition.Client!.Hello!;

        var network = new InMemoryNetwork(TimeProvider.System);
        var transport = network.CreateTransport("lonely", new TransportOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)));
        var runtime = new ClientRuntime(transport, new ClientOptions { BuildHash = 1 }, false, TimeProvider.System, NullLoggerFactory.Instance);
        var remote = new ClientDayNight(null, null, false);
        remote.Initialize(runtime);
        scene.Add(remote);
        var hello = real with { ServerTick = new Tick(1_000_000UL) };
        remote.OnServerHello(hello);
        AssertThat(await fixture.WaitUntil(() => remote.AppliedTick >= 1_000_001UL, 10)).IsTrue();

        AssertThat(remote.AppliedTick).IsLess(1_000_000UL + 600UL);
        AssertThat(Deviation(remote, hello.Planets[0], hello.TickRate)).IsLess(1e-4f);

        AssertThat(await fixture.StopAsync()).IsTrue();
    }
}
