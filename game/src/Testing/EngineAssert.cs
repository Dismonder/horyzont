namespace Horyzont.Game.Testing;

/// <summary>Minimal assertions for engine tests.</summary>
public static class EngineAssert
{
    /// <summary>Fails unless the condition holds.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="message">What was expected.</param>
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new EngineTestFailedException(message);
        }
    }

    /// <summary>Fails unless the values are equal.</summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="expected">Expected value.</param>
    /// <param name="actual">Actual value.</param>
    /// <param name="message">What is compared.</param>
    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new EngineTestFailedException($"{message}: expected {expected}, actual {actual}");
        }
    }

    /// <summary>Fails unless the value is at most the limit.</summary>
    /// <param name="actual">Measured value.</param>
    /// <param name="limit">Inclusive upper bound.</param>
    /// <param name="message">What is measured.</param>
    public static void AtMost(double actual, double limit, string message)
    {
        if (!(actual <= limit))
        {
            throw new EngineTestFailedException($"{message}: {actual:F2} exceeds {limit:F2}");
        }
    }
}
