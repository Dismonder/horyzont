using System.Globalization;

using GdUnit4;

using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class HostLoopTests
{
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Solo_SpawnsACharacter_ThatStandsOnTheGroundAndWalks()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await StartSoloAsync(scene, dir);
        var composition = fixture.Composition;
        AssertThat(fixture.StartExit).IsNull();
        AssertThat(await fixture.WaitInGame()).IsTrue();
        var player = composition.LocalPlayer!;

        AssertThat(await fixture.WaitUntil(() => player.IsActive, 120)).IsTrue();
        AssertThat(player.ServerMode).IsNotEqual(MovementMode.Frozen);
        var planet = composition.Server!.State.Planets.All[0];
        var standing = player.PredictedFeet;
        var generated = Horyzont.Game.Server.Characters.SpawnLocator.AboveSurface(planet, System.Numerics.Vector3.UnitX).Length() - Horyzont.Game.Server.Characters.SpawnLocator.SpawnClearance;
        Record($"solo_spawn standing_radius={standing.Length().ToString("F3", CultureInfo.InvariantCulture)} generated_surface={generated.ToString("F3", CultureInfo.InvariantCulture)} feet={standing}");
        AssertThat(Math.Abs(standing.Length() - planet.RadiusMeters)).IsLess(PlanetGeneratorV1ReliefMeters);
        AssertThat(composition.ServerCharacters!.Count).IsEqual(1);
        AssertThat(composition.Server.State.Players.TryGet(composition.Client!.Hello!.YourPlayer, out var state)).IsTrue();
        AssertThat(System.Numerics.Vector3.Distance(state!.Position, standing)).IsLess(0.5f);

        player.InputSource = new ScriptedInput(_ => new LocalInputSample(0f, 1f, InputButtons.Sprint, 0f, 0f));
        var start = player.ClientTick;
        AssertThat(await fixture.WaitUntil(() => player.ClientTick >= start + 120)).IsTrue();

        var moved = System.Numerics.Vector3.Distance(player.PredictedFeet, standing);
        AssertThat(moved).IsGreater(5f);
        AssertThat(player.CameraJumps).IsEqual(0);

        AssertThat(await fixture.StopAsync()).IsTrue();
        AssertThat(composition.ExitCode).IsEqual(0);
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task HostLoop_InMemory_PredictionMatchesServer_ZeroCorrections()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await StartSoloAsync(scene, dir);
        var composition = fixture.Composition;
        AssertThat(await fixture.WaitInGame()).IsTrue();
        var player = composition.LocalPlayer!;
        AssertThat(await fixture.WaitUntil(() => player.IsActive, 120)).IsTrue();

        var random = new Random(11);
        var held = new LocalInputSample(0f, 1f, InputButtons.None, 0f, 0f);
        player.InputSource = new ScriptedInput(n =>
        {
            if (n % 45 == 1)
            {
                var buttons = (random.Next(3) == 0 ? InputButtons.Sprint : InputButtons.None) | (random.Next(4) == 0 ? InputButtons.Jump : InputButtons.None);
                held = new LocalInputSample(random.Next(3) - 1, random.Next(3) - 1, buttons, 0f, 0f);
            }

            return held with { MouseYaw = (float)((random.NextDouble() - 0.5) * 0.05) };
        });
        AssertThat(await fixture.WaitUntil(() => player.ClientTick > 40)).IsTrue();

        // Collision that is still being rebuilt around the body can differ between the tick the client predicted and the later tick the
        // server processed the same input; that is a correction by design, so the measurement starts when the terrain has settled.
        AssertThat(await fixture.WaitTerrainIdle()).IsTrue();
        player.ResetStatistics();
        var start = player.ClientTick;
        AssertThat(await fixture.WaitUntil(() => player.ClientTick >= start + 600, 120)).IsTrue();

        Record($"host_loop corrections={player.Corrections} compared={player.Compared} max_error_mm={(player.MaxAckPositionError * 1000f).ToString("F3", CultureInfo.InvariantCulture)} jumps={player.CameraJumps} rejected={player.RejectedSnapshots} last=[{player.LastCorrection}]");
        AssertThat(player.Compared).IsGreater(100);
        AssertThat(player.Corrections).IsEqual(0);
        AssertThat(player.MaxAckPositionError).IsLess(0.001f);
        AssertThat(player.RejectedSnapshots).IsEqual(0);

        AssertThat(await fixture.StopAsync()).IsTrue();
    }

    /// <summary>
    /// Shutdown step 3a despawns the characters before the player leaves: the pose of the leave operation is the pose of the body at that
    /// moment, even when the body was moved after the last tick wrote it into the simulation.
    /// </summary>
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Solo_Shutdown_DespawnsBeforeLeave_LeavePoseIsTheBodyPose()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var fixture = await StartSoloAsync(scene, dir);
        var composition = fixture.Composition;
        AssertThat(await fixture.WaitInGame()).IsTrue();
        var player = composition.LocalPlayer!;
        AssertThat(await fixture.WaitUntil(() => player.IsActive, 120)).IsTrue();
        var playerId = composition.Client!.Hello!.YourPlayer;
        var characters = composition.ServerCharacters!;
        AssertThat(characters.Count).IsEqual(1);

        // Move the server body after the last step; only the despawn can carry this pose into the leave operation.
        var body = characters.GetNode<Horyzont.Game.Character.PlayerBody>($"Body_{playerId.Value}");
        var up = System.Numerics.Vector3.Normalize(body.Feet);
        var moved = body.Feet + (up * 0.75f);
        body.Place(moved, System.Numerics.Vector3.Zero, body.BodyRotation, MovementMode.Frozen, false);

        AssertThat(await fixture.StopAsync()).IsTrue();

        AssertThat(characters.Count).IsEqual(0);
        AssertThat(composition.Server!.State.Players.TryGet(playerId, out var state)).IsTrue();
        AssertThat(state!.IsOnline).IsFalse();
        AssertThat(System.Numerics.Vector3.Distance(state.Position, moved)).IsLess(1e-3f);
        AssertThat(composition.ExitCode).IsEqual(0);
    }

    private const float PlanetGeneratorV1ReliefMeters = 320f;

    private static Task<ModeFixture> StartSoloAsync(EngineTestScene scene, string dir, bool devBuild = false) =>
        ModeFixture.StartWithCharactersAsync(
            scene, dir, true, devBuild, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString());

    private static void Record(string line)
    {
        if (System.Environment.GetEnvironmentVariable("HORYZONT_RECON_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + "\n");
        }
    }

    internal sealed class ScriptedInput(Func<uint, LocalInputSample> sample) : ILocalInputSource
    {
        private uint _n;

        public LocalInputSample Take() => sample(++_n);
    }
}
