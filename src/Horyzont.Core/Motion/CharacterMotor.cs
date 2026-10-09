using System.Numerics;

using Horyzont.Core.World;

namespace Horyzont.Core.Motion;

/// <summary>Pure movement step shared by the server and the client prediction. Allocation-free.</summary>
public static class CharacterMotor
{
    /// <summary>
    /// One tick of movement. <paramref name="dt"/> is 1 / <c>GameRules.TickRate</c>. The caller applies
    /// <see cref="MotorOutput.DesiredVelocity"/> (MoveAndSlide in the engine, an ideal integrator in tests).
    /// </summary>
    /// <param name="input">Validated input.</param>
    /// <param name="context">Body state before the step.</param>
    /// <param name="rules">Movement rules.</param>
    /// <param name="dt">Tick length in seconds.</param>
    /// <returns>The wanted velocity, up, body rotation and mode.</returns>
    public static MotorOutput Step(in MotorInput input, in MotorContext context, MovementRules rules, float dt) =>
        throw new NotImplementedException();

    /// <summary>
    /// Gravity: -UpAt(feet) * Planet.Gravity while |feet| is at most the radius plus the atmosphere height, otherwise zero.
    /// </summary>
    /// <param name="feet">Feet position in the planet frame.</param>
    /// <param name="planet">Planet.</param>
    /// <returns>The gravity vector in m/s².</returns>
    public static Vector3 GravityAt(Vector3 feet, PlanetParams planet) => throw new NotImplementedException();
}
