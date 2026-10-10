using System.Numerics;

using GdUnit4;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Physics;
using Horyzont.Game.Tests.Character.Fixtures;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class ServerCharactersTests
{
    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_LogoutAndRejoinOnRoof_KeepsSavedFeet()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene);
        var roofTop = TerrainTestWorld.Surface + 5f;
        var roof = scene.Add(new Godot.StaticBody3D
        {
            Position = new Godot.Vector3(0, roofTop - 0.5, 0),
            CollisionLayer = CollisionLayers.ConstructsMask,
            CollisionMask = 0,
        });
        roof.AddChild(new Godot.CollisionShape3D { Shape = new Godot.BoxShape3D { Size = new Godot.Vector3(10, 1, 10) } });
        var feet = new Vector3(0, roofTop + 0.01f, 0);
        var player = rig.Join(1, feet);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();
        AssertThat(Vector3.Distance(rig.BodyOf(player).Feet, feet)).IsLess(0.1f);
        rig.Characters.Despawn(player);
        rig.Sim.State.Players.TryGet(player, out var saved);
        var lastFeet = saved!.Position;
        rig.Sim.ApplyServerOperation(new PlayerLeaveOp(player, saved.Frame, saved.ParentSeq, saved.Position, saved.BodyRotation, saved.Pitch));
        var join = rig.Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, 1), "player1");
        rig.Sim.ApplyServerOperation(join);
        rig.Characters.Spawn(join.Player);
        AssertThat(await rig.WaitUnfrozenAsync(join.Player)).IsTrue();
        AssertThat(Vector3.Distance(rig.BodyOf(join.Player).Feet, lastFeet)).IsLess(0.1f);
        AssertThat(rig.BodyOf(join.Player).Feet.Y).IsGreater(roofTop - 0.05f);
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_TerrainReadyBeforeRoof_StaysAtSavedFeetBeforeDeadline()
    {
        await using var scene = EngineTestScene.Create();
        var collisionReady = false;
        await using var rig = await CharacterRig.CreateAsync(scene, movement: new MovementRules { GroundWaitDeadlineTicks = 300 },
            constructsReady: (_, _) => collisionReady);
        var roofTop = TerrainTestWorld.Surface + 5f;
        var feet = new Vector3(0, roofTop + 0.01f, 0);
        var player = rig.Join(1, feet);
        AssertThat(await rig.WaitTicks(rig.Ticks + 90)).IsTrue();
        AssertThat(rig.Characters.TryGetState(player, out var frozen)).IsTrue();
        AssertThat(frozen.Mode).IsEqual(MovementMode.Frozen);
        AssertThat(frozen.Feet).IsEqual(feet);
        var roof = scene.Add(new Godot.StaticBody3D
        {
            Position = new Godot.Vector3(0, roofTop - 0.5, 0),
            CollisionLayer = CollisionLayers.ConstructsMask,
            CollisionMask = 0,
        });
        roof.AddChild(new Godot.CollisionShape3D { Shape = new Godot.BoxShape3D { Size = new Godot.Vector3(10, 1, 10) } });
        await scene.WaitFrames(3);
        collisionReady = true;
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();
        AssertThat(Vector3.Distance(rig.BodyOf(player).Feet, feet)).IsLess(0.1f);
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_ConstructCollisionNeverReady_ExpiresWithoutTeleporting()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene, movement: new MovementRules { GroundWaitDeadlineTicks = 5 },
            constructsReady: (_, _) => false);
        var feet = new Vector3(0, TerrainTestWorld.Surface + 5, 0);
        var player = rig.Join(1, feet);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();
        AssertThat(rig.Characters.TryGetState(player, out var released)).IsTrue();
        AssertThat(released.Mode).IsEqual(MovementMode.Falling);
        AssertThat(released.Feet).IsEqual(feet);
        AssertThat(rig.Log.Count(9306)).IsEqual(1);
    }

    private static readonly Vector3 _start = new(0f, TerrainTestWorld.Surface + 1.5f, 0f);

    private static PlayerInput Forward(uint tick, float yaw = 0f) => new(tick, 0f, 1f, yaw, 0f, InputButtons.None);

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_OneInputPerTick_ExcessDropped()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene);
        var player = rig.Join(1, _start);
        for (uint seq = 1; seq <= 40; seq++)
        {
            rig.Inputs.Enqueue(player, seq, Forward(seq));
        }

        var seen = new List<uint>();
        rig.AfterStep = _ =>
        {
            if (rig.Characters.TryGetState(player, out var state))
            {
                seen.Add(state.LastProcessedInputSeq);
            }
        };

        AssertThat(await rig.WaitTicks(rig.Ticks + 40)).IsTrue();
        rig.AfterStep = null;

        // Exactly one input is consumed per step, in order, and the acknowledged number follows.
        AssertThat(seen.Count).IsGreaterEqual(40);
        for (var i = 1; i < 40; i++)
        {
            AssertThat(seen[i] - seen[i - 1]).IsLessEqual(1u);
        }

        AssertThat(rig.Inputs.Pending(player) + (int)seen[^1]).IsEqual(40);
        AssertThat(seen[^1]).IsGreater(0u);
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_Starvation_NeutralAfterSixTicks()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene);
        var player = rig.Join(1, _start);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();

        // Hold the body in the air, then feed nothing: it stands still for 6 ticks and falls from the 7th.
        var body = rig.BodyOf(player);
        var feet = new Vector3(0f, TerrainTestWorld.Surface + 3f, 0f);
        var heights = new List<float>();
        var placed = false;
        rig.BeforeStep = _ =>
        {
            if (!placed)
            {
                placed = true;
                body.Place(feet, Vector3.Zero, SurfaceFrame.FromYaw(Vector3.UnitY, 0f), MovementMode.Falling, false);

                // One input resets the starvation counter: this tick moves the body, the six after it do not.
                rig.Inputs.Enqueue(player, 5000, new PlayerInput(1, 0f, 0f, 0f, 0f, InputButtons.None));
            }
        };
        rig.AfterStep = _ =>
        {
            if (placed)
            {
                heights.Add(body.Feet.Y);
            }
        };

        var start = rig.Ticks;
        AssertThat(await rig.WaitTicks(start + 14)).IsTrue();
        rig.BeforeStep = null;
        rig.AfterStep = null;

        // heights[0] is the tick that consumed the input; the next six ticks without input keep the body where it is.
        AssertThat(heights[0]).IsLess(feet.Y);
        for (var i = 1; i <= 6; i++)
        {
            AssertThat(heights[i]).IsEqual(heights[0]);
        }

        AssertThat(heights[7]).IsLess(heights[0]);
        AssertThat(heights[10]).IsLess(heights[7]);
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_EveryTick_UpdatesKinematicsWithNormalizedRotation()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene);
        var player = rig.Join(1, _start);
        var random = new Random(5);
        uint seq = 0;
        var bad = 0;
        var checkedTicks = 0;
        rig.BeforeStep = tick => rig.Inputs.Enqueue(player, ++seq, Forward((uint)tick, (float)((random.NextDouble() * 2d) - 1d) * 3f));
        rig.AfterStep = _ =>
        {
            rig.Sim.State.Players.TryGet(player, out var state);
            var body = rig.BodyOf(player);
            checkedTicks++;
            if (MathF.Abs(state!.BodyRotation.Length() - 1f) > 1e-6f || Vector3.Distance(state.Position, body.Feet) > 1e-4f
                || state.Frame != Horyzont.Core.Ids.FrameRef.Of(Horyzont.Game.Tests.Terrain.TerrainTestWorld.Planet.Id))
            {
                bad++;
            }
        };

        var start = rig.Ticks;
        AssertThat(await rig.WaitTicks(start + 90)).IsTrue();
        rig.BeforeStep = null;
        rig.AfterStep = null;

        AssertThat(checkedTicks).IsGreaterEqual(90);
        AssertThat(bad).IsEqual(0);
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_GroundWaitDeadline_UnfreezesAndLogs()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene, movement: new MovementRules { GroundWaitDeadlineTicks = 30 });

        // 200 m up: no collision is built there, so the ground wait cannot succeed.
        var player = rig.Join(1, new Vector3(0f, TerrainTestWorld.Surface + 200f, 0f));
        AssertThat(rig.Characters.TryGetState(player, out var spawned)).IsTrue();
        AssertThat(spawned.Mode).IsEqual(MovementMode.Frozen);

        AssertThat(await rig.WaitTicks(rig.Ticks + 28)).IsTrue();
        AssertThat(rig.Characters.TryGetState(player, out var waiting)).IsTrue();
        AssertThat(waiting.Mode).IsEqual(MovementMode.Frozen);
        AssertThat(rig.Log.Count(9306)).IsEqual(0);

        AssertThat(await rig.WaitTicks(rig.Ticks + 10)).IsTrue();
        AssertThat(rig.Characters.TryGetState(player, out var released)).IsTrue();
        AssertThat(released.Mode).IsEqual(MovementMode.Falling);
        AssertThat(rig.Log.Count(9306)).IsEqual(1);
    }

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_BelowCore_RescuedToSurface()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene, rescue: (planet, feet) => Vector3.Normalize(feet) * (planet.RadiusMeters + 2f));
        var player = rig.Join(1, _start);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();

        var body = rig.BodyOf(player);
        var planet = TerrainTestWorld.Planet;
        var below = new Vector3(0f, planet.RadiusMeters - planet.CoreDepthMeters - 20f, 0f);
        var placed = false;
        MovementMode? afterRescue = null;
        var radiusAfterRescue = 0f;
        rig.BeforeStep = _ =>
        {
            if (!placed)
            {
                placed = true;
                body.Place(below, Vector3.Zero, SurfaceFrame.FromYaw(Vector3.UnitY, 0f), MovementMode.Falling, false);
                rig.Inputs.Enqueue(player, 1000, Forward(1));
            }
        };
        rig.AfterStep = _ =>
        {
            if (placed && afterRescue is null)
            {
                afterRescue = body.Mode;
                radiusAfterRescue = body.Feet.Length();
            }
        };

        AssertThat(await rig.WaitTicks(rig.Ticks + 3)).IsTrue();
        rig.BeforeStep = null;
        rig.AfterStep = null;

        // The tick that found the body under the core puts it above the surface and freezes it; the next ticks find the ground again.
        AssertThat(afterRescue).IsEqual(MovementMode.Frozen);
        AssertThat(radiusAfterRescue).IsGreater(planet.RadiusMeters);
        AssertThat(rig.Log.Count(9307)).IsEqual(1);
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_Despawn_WritesTheLastKinematicsAndFreesTheBody()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene);
        var player = rig.Join(1, _start);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();
        AssertThat(await rig.WaitTicks(rig.Ticks + 5)).IsTrue();
        var body = rig.BodyOf(player);
        var feet = body.Feet;

        rig.Characters.Despawn(player);

        AssertThat(rig.Characters.Count).IsEqual(0);
        AssertThat(rig.Characters.TryGetState(player, out _)).IsFalse();
        AssertThat(rig.Sim.State.Players.TryGet(player, out var state)).IsTrue();
        AssertThat(Vector3.Distance(state!.Position, feet)).IsLess(1e-4f);
        AssertThat(rig.Characters.GetNodeOrNull($"Body_{player.Value}")).IsNull();
        await scene.WaitFrames(2);
    }

    [TestCase(Timeout = 400000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_DevFlyBit_IgnoredWithoutConsent_HonouredWithIt()
    {
        foreach (var allowed in new[] { false, true })
        {
            await using var scene = EngineTestScene.Create();
            await using var rig = await CharacterRig.CreateAsync(scene, devFlight: allowed);
            var player = rig.Join(1, _start);
            AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();
            uint seq = 0;
            rig.BeforeStep = tick => rig.Inputs.Enqueue(player, ++seq, new PlayerInput((uint)tick, 0f, 0f, 0f, 0f, InputButtons.DevFly));
            AssertThat(await rig.WaitTicks(rig.Ticks + 10)).IsTrue();
            rig.BeforeStep = null;

            AssertThat(rig.Characters.TryGetState(player, out var state)).IsTrue();
            AssertThat(state.Mode == MovementMode.DevFlying).IsEqual(allowed);
        }
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task ServerCharacters_Step_EightPlayers_DoesNotAllocate()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene);
        var players = new List<PlayerId>();
        for (ulong i = 0; i < 8; i++)
        {
            players.Add(rig.Join(i + 1, new Vector3(i * 3f, TerrainTestWorld.Surface + 1.5f, 0f)));
        }

        foreach (var player in players)
        {
            AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();
        }

        uint seq = 0;
        long allocated = 0;
        var measuring = false;
        var measured = 0;
        rig.BeforeStep = tick =>
        {
            seq++;
            foreach (var player in players)
            {
                rig.Inputs.Enqueue(player, seq, new PlayerInput((uint)tick, 0f, 1f, (seq % 200) * 0.03f, 0f, InputButtons.Sprint));
            }
        };

        AssertThat(await rig.WaitTicks(rig.Ticks + 120)).IsTrue();
        long before = 0;
        rig.BeforeStep = tick =>
        {
            seq++;
            foreach (var player in players)
            {
                rig.Inputs.Enqueue(player, seq, new PlayerInput((uint)tick, 0f, 1f, (seq % 200) * 0.03f, 0f, InputButtons.Sprint));
            }

            before = GC.GetAllocatedBytesForCurrentThread();
        };
        rig.AfterStep = _ =>
        {
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            measured++;
            measuring = true;
        };

        AssertThat(await rig.WaitTicks(rig.Ticks + 600)).IsTrue();
        rig.BeforeStep = null;
        rig.AfterStep = null;

        Record($"eight_players allocated_bytes={allocated} ticks={measured} measuring={measuring}");
        AssertThat(measured).IsGreaterEqual(600);
        AssertThat(allocated).IsEqual(0L);
    }

    private static void Record(string line)
    {
        if (System.Environment.GetEnvironmentVariable("HORYZONT_RECON_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + "\n");
        }
    }
}
