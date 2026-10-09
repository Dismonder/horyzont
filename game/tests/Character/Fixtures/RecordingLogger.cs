using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Tests.Character.Fixtures;

/// <summary>A logger that keeps the entries, for tests that check what was reported.</summary>
/// <typeparam name="T">The category.</typeparam>
public sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>Gets the entries: level, event id and the formatted message.</summary>
    public List<(LogLevel Level, int EventId, string Message)> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, eventId.Id, formatter(state, exception)));

    /// <summary>Counts the entries with an event id.</summary>
    /// <param name="eventId">The event id.</param>
    /// <returns>How many were logged.</returns>
    public int Count(int eventId) => Entries.Count(e => e.EventId == eventId);
}
