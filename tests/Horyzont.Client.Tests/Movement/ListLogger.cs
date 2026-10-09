using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Tests.Movement;

/// <summary>Logger that records every enabled entry.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, int EventId, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, eventId.Id, formatter(state, exception)));
}
