using System.Numerics;

using GdUnit4;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Tests.Character.Fixtures;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;
using Horyzont.Testing;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Ships;

/// <summary>
/// K5-25 criterion 4 and decision 19 (R4) on real terrain collision: a player attached in a construct 40 m over the flat ground is
/// ejected into the planet frame with a velocity; the server body glides (Gliding, inherited velocity, fall speed at most 6 m/s) without
/// the frozen ground wait and walks after touching the ground.
/// </summary>
[TestSuite]
public class EjectEngineTests
{
    private const float Height = 40f;
    private const int MaxTicks = 60 * 30;

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task Eject_HomeTerrain_LandsGliding_FallSpeedAtMost6()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene, ejects: true);
        var feet = new Vector3(0, TerrainTestWorld.Surface + 0.01f, 0);
        var player = rig.Join(1, feet);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();

        var sim = rig.Sim;
        sim.State.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone);
        var construct = sim.State.EntityIds.PeekNext();
        sim.ApplyServerOperation(new ConstructCreateOp(
            construct, FrameRef.Of(TerrainTestWorld.Planet.Id), new Vector3(0, TerrainTestWorld.Surface + Height, 0), Quaternion.Identity,
            new CellPos(0, 0, 0), BlockState.Create(stone, 0)));
        sim.State.Players.TryGet(player, out var state);
        var entity = state!.Entity;
        sim.ApplyServerOperation(new EntityReparentOp(
            entity, FrameRef.Of(construct), state.ParentSeq + 1, new Vector3(0.5f, 1.05f, 0.5f), Quaternion.Identity, Vector3.Zero, new CellPos(0, 0, 0)));
        AssertThat(await rig.WaitTicks(rig.Ticks + 5)).IsTrue();

        var sawGlide = false;
        var landed = false;
        var maxFall = 0f;
        var maxHorizontal = 0f;
        rig.AfterStep = _ =>
        {
            if (!rig.Characters.TryGetState(player, out var body) || body.Frame.Kind != FrameKind.Planet)
            {
                return;
            }

            var up = Vector3.Normalize(body.Feet);
            if (body.Mode == MovementMode.Gliding)
            {
                sawGlide = true;
                maxFall = MathF.Max(maxFall, -Vector3.Dot(body.Velocity, up));
                maxHorizontal = MathF.Max(maxHorizontal, (body.Velocity - (Vector3.Dot(body.Velocity, up) * up)).Length());
            }
            else if (sawGlide && body.Mode == MovementMode.Walking)
            {
                landed = true;
            }
        };

        sim.ApplyServerOperation(new EntityReparentOp(
            entity, FrameRef.Of(TerrainTestWorld.Planet.Id), state.ParentSeq + 1, new Vector3(3f, TerrainTestWorld.Surface + Height + 1f, 0f),
            Quaternion.Identity, new Vector3(12f, 0f, 0f), null));
        var start = rig.Ticks;
        while (!landed && rig.Ticks - start < MaxTicks)
        {
            await rig.WaitTicks(rig.Ticks + 30);
        }

        rig.AfterStep = null;
        AssertThat(sawGlide).IsTrue();
        AssertThat(landed).IsTrue();
        AssertThat(maxFall <= new MovementRules().SuitGlideFallSpeed + 0.05f).OverrideFailureMessage($"fall {maxFall} m/s").IsTrue();
        AssertThat(maxHorizontal > 5f).OverrideFailureMessage($"horizontal {maxHorizontal} m/s").IsTrue();
    }
}
