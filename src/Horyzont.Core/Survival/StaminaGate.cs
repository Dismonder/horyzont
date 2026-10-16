using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Core.Survival;

/// <summary>Takes sprinting and jumping from an exhausted player (K6-33, D20); the server and the local player apply it to the input before the motor.</summary>
public static class StaminaGate
{
    /// <summary>Filters the buttons of the input.</summary>
    /// <param name="input">The input.</param>
    /// <param name="exhausted">Whether the player is exhausted.</param>
    /// <returns>The input without <see cref="InputButtons.Sprint"/> and <see cref="InputButtons.Jump"/> when exhausted, else unchanged.</returns>
    public static MotorInput Filter(in MotorInput input, bool exhausted) => throw new NotImplementedException("K6-33 A6");
}
