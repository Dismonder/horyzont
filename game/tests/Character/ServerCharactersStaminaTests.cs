using System.Numerics;

using GdUnit4;

using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Tests.Character.Fixtures;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

/// <summary>The server body ignores the sprint and the jump of an exhausted player (K6-33 D20), and obeys them when the player is not exhausted.</summary>
[TestSuite]
public class ServerCharactersStaminaTests
{
    private static readonly Vector3 _start = new(0f, TerrainTestWorld.Surface + 1.5f, 0f);

    [TestCase(Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task Exhausted_SprintAndJumpIgnoredByServerBody()
    {
        await using var scene = EngineTestScene.Create();
        await using var rig = await CharacterRig.CreateAsync(scene);
        var player = rig.Join(1, _start);
        AssertThat(await rig.WaitUnfrozenAsync(player)).IsTrue();
        var exhausted = false;
        rig.Characters.ExhaustedCheck = _ => exhausted;
        uint seq = 1000;
        rig.BeforeStep = tick => rig.Inputs.Enqueue(player, ++seq, new PlayerInput((uint)tick, 0f, 1f, 0f, 0f, InputButtons.Sprint | InputButtons.Jump));
        var modes = new HashSet<MovementMode>();
        rig.AfterStep = _ =>
        {
            if (rig.Characters.TryGetState(player, out var state))
            {
                modes.Add(state.Mode);
            }
        };

        // The twin: a player who is not exhausted sprints or jumps with the same input.
        AssertThat(await rig.WaitTicks(rig.Ticks + 90)).IsTrue();
        AssertThat(modes.Contains(MovementMode.Sprinting) || modes.Contains(MovementMode.Jumping)).IsTrue();

        // The same input from an exhausted player never leaves walking or falling (it lands first if it was in the air).
        exhausted = true;
        AssertThat(await rig.WaitTicks(rig.Ticks + 240)).IsTrue();
        modes.Clear();
        AssertThat(await rig.WaitTicks(rig.Ticks + 120)).IsTrue();
        rig.BeforeStep = null;
        rig.AfterStep = null;

        AssertThat(modes.Contains(MovementMode.Sprinting)).IsFalse();
        AssertThat(modes.Contains(MovementMode.Jumping)).IsFalse();
        AssertThat(modes.Contains(MovementMode.Walking)).IsTrue();
    }
}
