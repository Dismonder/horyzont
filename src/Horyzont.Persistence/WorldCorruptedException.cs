using Horyzont.Core.Ids;

namespace Horyzont.Persistence;

/// <summary>The world file is damaged or inconsistent; the game offers to restore a backup.</summary>
public sealed class WorldCorruptedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is damaged.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public WorldCorruptedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a damaged journal entry.</summary>
    /// <param name="opId">Number of the damaged or missing operation.</param>
    /// <param name="message">What is damaged.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public WorldCorruptedException(OpId opId, string message, Exception? innerException = null)
        : base($"Operation {opId.Value}: {message}", innerException)
    {
        OpId = opId;
    }

    /// <summary>The damaged or missing journal operation; null when the damage is outside the journal.</summary>
    public OpId? OpId { get; }
}
