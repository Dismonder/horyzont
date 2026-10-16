using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-33, D20: an exhausted player cannot sprint or jump; the server and the local player filter the buttons with the same function.</summary>
public sealed class StaminaGateTests
{
    private static readonly MotorInput _input = new(0.5f, -1f, 1.25f, 0.5f, InputButtons.Jump | InputButtons.Sprint | InputButtons.Crouch);

    [Fact]
    public void Filter_Exhausted_ClearsSprintAndJumpOnly()
    {
        // The accepted counterpart: the same input is unchanged for a player who is not exhausted.
        Assert.Equal(_input, StaminaGate.Filter(_input, false));

        var filtered = StaminaGate.Filter(_input, true);

        Assert.Equal(InputButtons.Crouch, filtered.Buttons);
        Assert.Equal(_input with { Buttons = InputButtons.Crouch }, filtered);
    }

    [Fact]
    public void Filter_NotExhausted_Unchanged()
    {
        foreach (var buttons in new[] { InputButtons.None, InputButtons.Jump, InputButtons.Sprint, InputButtons.Jump | InputButtons.Sprint, InputButtons.Crouch })
        {
            var input = _input with { Buttons = buttons };
            Assert.Equal(input, StaminaGate.Filter(input, false));
        }
    }

    [Fact]
    public void Filter_ExhaustedWithoutThoseButtons_Unchanged()
    {
        var crouching = _input with { Buttons = InputButtons.Crouch };

        Assert.Equal(crouching, StaminaGate.Filter(crouching, true));
        Assert.Equal(_input with { Buttons = InputButtons.None }, StaminaGate.Filter(_input with { Buttons = InputButtons.None }, true));
        Assert.Equal(_input with { Buttons = InputButtons.None }, StaminaGate.Filter(_input with { Buttons = InputButtons.Jump }, true));
        Assert.Equal(_input with { Buttons = InputButtons.None }, StaminaGate.Filter(_input with { Buttons = InputButtons.Sprint }, true));
    }
}
