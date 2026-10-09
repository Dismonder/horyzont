namespace Horyzont.Game.Testing;

/// <summary>Thrown by <see cref="EngineAssert"/> when an expectation does not hold.</summary>
public sealed class EngineTestFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public EngineTestFailedException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Description of the failed expectation.</param>
    public EngineTestFailedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Description of the failed expectation.</param>
    /// <param name="innerException">The cause.</param>
    public EngineTestFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
