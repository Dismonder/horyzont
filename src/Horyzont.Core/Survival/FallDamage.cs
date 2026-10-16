namespace Horyzont.Core.Survival;

/// <summary>The damage of a fall as a pure function of the impact speed (K6-33, D18).</summary>
public static class FallDamage
{
    /// <summary>Computes the damage.</summary>
    /// <param name="impactSpeed">The impact speed in m/s.</param>
    /// <param name="rules">The rules.</param>
    /// <returns><c>max(0, (impactSpeed - FallSafeSpeed) * FallDamagePerMetrePerSecond)</c>.</returns>
    public static float Compute(float impactSpeed, VitalsRules rules) => throw new NotImplementedException("K6-33 A6");
}
