using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Tests.Support;

/// <summary>A logger factory that keeps every entry for assertions.</summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = [];

    public sealed record LogEntry(string Category, LogLevel Level, EventId Event, string Message, Exception? Exception);

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public bool Contains(LogLevel level, string text) =>
        Entries.Any(e => e.Level == level && e.Message.Contains(text, StringComparison.Ordinal));

    public bool Contains(string text) => Entries.Any(e => e.Message.Contains(text, StringComparison.Ordinal));

    public void AddProvider(ILoggerProvider provider)
    {
        // Not needed: this factory is its own only provider.
    }

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(RecordingLoggerFactory owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._gate)
            {
                owner._entries.Add(new LogEntry(category, logLevel, eventId, formatter(state, exception), exception));
            }
        }
    }
}
