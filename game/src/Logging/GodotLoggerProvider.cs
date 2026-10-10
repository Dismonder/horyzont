using System.Globalization;
using System.Text;

using Godot;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> of the game: every entry goes to the Godot console (<see cref="GD.Print(string)"/>, warnings and
/// errors to stderr) and, when given, to a <see cref="RotatingLogFile"/>. Thread-safe.
/// The engine console is used from the creating (main) thread only and until <see cref="Shutdown"/>: entries from other threads
/// go to the process standard streams, and after the shutdown to the file and, for warnings and errors, to stderr. The engine
/// print functions are not safe while the tree quits and the engine tears down.
/// </summary>
public sealed class GodotLoggerProvider : ILoggerProvider
{
    private readonly RotatingLogFile? _file;
    private readonly LogLevel _minimum;
    private readonly TimeProvider _time;
    private readonly Action<string, bool> _console;
    private readonly Action<string, bool> _fallback;
    private readonly int _mainThreadId = System.Environment.CurrentManagedThreadId;
    private int _shutdown;

    /// <summary>Creates the provider.</summary>
    /// <param name="file">The log file; null logs to the console only. The provider owns and disposes it.</param>
    /// <param name="minimum">Lowest level that is written.</param>
    /// <param name="time">Clock of the time stamps.</param>
    /// <param name="console">Console sink (text, is warning or error); the Godot console when null. Used on the creating thread before the shutdown.</param>
    /// <param name="fallback">Sink for other threads and for warnings after the shutdown; the standard streams when null.</param>
    public GodotLoggerProvider(RotatingLogFile? file, LogLevel minimum, TimeProvider time, Action<string, bool>? console = null, Action<string, bool>? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        _file = file;
        _minimum = minimum;
        _time = time;
        _console = console ?? WriteToGodot;
        _fallback = fallback ?? WriteToStandardStreams;
    }

    /// <summary>
    /// Call before the tree quits (any thread): from now on the engine console is not touched. Entries still go to the file, and
    /// warnings and errors to stderr.
    /// </summary>
    public void Shutdown() => Volatile.Write(ref _shutdown, 1);

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new GodotLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose() => _file?.Dispose();

    private static void WriteToGodot(string text, bool isError)
    {
        if (isError)
        {
            GD.PrintErr(text);
        }
        else
        {
            GD.Print(text);
        }
    }

    private static void WriteToStandardStreams(string text, bool isError)
    {
        try
        {
            (isError ? System.Console.Error : System.Console.Out).WriteLine(text);
        }
        catch (IOException)
        {
            // The stream is closed while the process ends; the file has the entry.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string Name(LogLevel level) => level switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Info",
        LogLevel.Warning => "Warn",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Crit",
        _ => "None",
    };

    private void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        var text = new StringBuilder(96 + message.Length);
        text.Append(_time.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        text.Append(" [").Append(Name(level)).Append("] ").Append(category);
        if (eventId.Id != 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" #{eventId.Id}");
        }

        text.Append(": ").Append(message);
        if (exception is not null)
        {
            text.Append('\n').Append(exception);
        }

        var line = text.ToString();
        _file?.WriteLine(line);
        var isError = level >= LogLevel.Warning;
        if (Volatile.Read(ref _shutdown) != 0)
        {
            if (isError)
            {
                _fallback(line, true);
            }
        }
        else if (System.Environment.CurrentManagedThreadId == _mainThreadId)
        {
            _console(line, isError);
        }
        else
        {
            _fallback(line, isError);
        }
    }

    private sealed class GodotLogger(GodotLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider._minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(logLevel, category, eventId, formatter(state, exception), exception);
        }
    }
}
