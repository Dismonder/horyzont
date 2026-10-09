using Horyzont.Server.Console;
using Horyzont.Server.Tests.Support;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests;

public sealed class ConsoleInputPumpTests
{
    [Fact]
    public void Lines_ArriveInOrderOnTheMainThread()
    {
        var pump = new ConsoleInputPump(new StringReader("save\nplayers\nstop\n"), NullLogger<ConsoleInputPump>.Instance);

        pump.Start();

        Assert.True(pump.WaitForEnd(TimeSpan.FromSeconds(5)));
        var lines = new List<string>();
        while (pump.TryTake(out var line))
        {
            lines.Add(line);
        }

        Assert.Equal(["save", "players", "stop"], lines);
        Assert.Equal(0, pump.DroppedLines);
    }

    [Fact]
    public async Task Eof_EndsPumpServerKeepsRunning()
    {
        await using var rig = new ServerRig(mode: ProcessMode.DedicatedServer);
        await rig.StartAsync();
        var processor = new ConsoleCommandProcessor(rig.Runtime, NullLogger<ConsoleCommandProcessor>.Instance);
        var pump = new ConsoleInputPump(new StringReader("players\n"), NullLogger<ConsoleInputPump>.Instance);

        pump.Start();
        Assert.True(pump.WaitForEnd(TimeSpan.FromSeconds(5)));
        Assert.True(pump.HasEnded);
        var replies = new List<string>();
        while (pump.TryTake(out var line))
        {
            replies.Add(processor.Execute(line));
        }

        Assert.Equal(["0 players"], replies);
        rig.StepMany(30);
        Assert.Equal(ServerRuntimeState.Running, rig.Runtime.RuntimeState);
        await rig.StopAsync();
    }

    [Fact]
    public void Eof_IsLoggedAndEndsThePump()
    {
        var log = new RecordingLoggerFactory();
        var pump = new ConsoleInputPump(new StringReader(string.Empty), new TypedLogger<ConsoleInputPump>(log));

        pump.Start();

        Assert.True(pump.WaitForEnd(TimeSpan.FromSeconds(5)));
        Assert.True(log.Contains("Console input ended"));
    }

    [Fact]
    public void Overflow_DropsWithWarning()
    {
        var log = new RecordingLoggerFactory();
        var input = string.Join('\n', Enumerable.Range(0, 100).Select(i => "line" + i)) + "\n";
        var pump = new ConsoleInputPump(new StringReader(input), new TypedLogger<ConsoleInputPump>(log));

        pump.Start();

        Assert.True(pump.WaitForEnd(TimeSpan.FromSeconds(5)));
        var taken = new List<string>();
        while (pump.TryTake(out var line))
        {
            taken.Add(line);
        }

        Assert.Equal(ConsoleInputPump.QueueCapacity, taken.Count);
        Assert.Equal("line0", taken[0]);
        Assert.Equal(100 - ConsoleInputPump.QueueCapacity, pump.DroppedLines);
        Assert.Equal(100 - ConsoleInputPump.QueueCapacity, log.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("queue of 64 lines is full", StringComparison.Ordinal)));
    }

    [Fact]
    public void OverlongLine_IsDroppedWithWarning()
    {
        var log = new RecordingLoggerFactory();
        var pump = new ConsoleInputPump(new StringReader(new string('x', 300) + "\nsave\n"), new TypedLogger<ConsoleInputPump>(log));

        pump.Start();

        Assert.True(pump.WaitForEnd(TimeSpan.FromSeconds(5)));
        Assert.True(pump.TryTake(out var line));
        Assert.Equal("save", line);
        Assert.False(pump.TryTake(out _));
        Assert.Equal(1, pump.DroppedLines);
        Assert.True(log.Contains(LogLevel.Warning, "longer than 256"));
    }

    [Fact]
    public void ReadFailure_EndsThePumpWithAnErrorLog()
    {
        var log = new RecordingLoggerFactory();
        var pump = new ConsoleInputPump(new FailingReader(), new TypedLogger<ConsoleInputPump>(log));

        pump.Start();

        Assert.True(pump.WaitForEnd(TimeSpan.FromSeconds(5)));
        Assert.True(log.Contains(LogLevel.Warning, "Reading the console failed"));
    }

    private sealed class FailingReader : TextReader
    {
        public override string ReadLine() => throw new IOException("stdin closed (test)");
    }

    private sealed class TypedLogger<T>(ILoggerFactory factory) : ILogger<T>
    {
        private readonly ILogger _inner = factory.CreateLogger(typeof(T).FullName!);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
