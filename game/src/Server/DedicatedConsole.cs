using Horyzont.Server.Console;

namespace Horyzont.Game.Server;

/// <summary>
/// The console of a dedicated server: the input pump reads standard input on its own thread, the lines are executed here on the
/// main thread (<see cref="ConsoleCommandProcessor"/>) and the answers are printed with the given sink.
/// </summary>
public sealed class DedicatedConsole
{
    private readonly ConsoleInputPump _pump;
    private readonly ConsoleCommandProcessor _processor;
    private readonly Action<string> _print;

    /// <summary>Creates the console.</summary>
    /// <param name="pump">Reader of standard input.</param>
    /// <param name="processor">Executor of the commands.</param>
    /// <param name="print">Where the answers go (the Godot console).</param>
    public DedicatedConsole(ConsoleInputPump pump, ConsoleCommandProcessor processor, Action<string> print)
    {
        ArgumentNullException.ThrowIfNull(pump);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(print);
        _pump = pump;
        _processor = processor;
        _print = print;
    }

    /// <summary>Starts reading standard input.</summary>
    public void Start() => _pump.Start();

    /// <summary>Stops reading standard input before the process quits (a short join; the read itself cannot be interrupted).</summary>
    public void Stop() => _pump.Stop(TimeSpan.FromMilliseconds(100));

    /// <summary>Executes the lines read so far; main thread, once per frame.</summary>
    public void Pump()
    {
        while (_pump.TryTake(out var line))
        {
            var answer = _processor.Execute(line);
            if (answer.Length > 0)
            {
                _print(answer);
            }
        }
    }
}
