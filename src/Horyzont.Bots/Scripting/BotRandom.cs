namespace Horyzont.Bots.Scripting;

/// <summary>Stateless pseudo-random numbers (SplitMix64 finalizer): the same inputs always give the same value. Client side only.</summary>
internal static class BotRandom
{
    public static ulong Mix(ulong seed, ulong a, ulong b = 0)
    {
        var z = seed + (0x9E3779B97F4A7C15UL * (a + 1)) + (0xBF58476D1CE4E5B9UL * (b + 1));
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A yaw in (-pi, pi].</summary>
    public static float Yaw(ulong seed, ulong a, ulong b = 0)
    {
        var unit = (Mix(seed, a, b) >> 11) * (1.0 / (1UL << 53));
        return (float)(Math.PI - (unit * 2.0 * Math.PI));
    }
}
