using System.Threading.Channels;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Console;

/// <summary>
/// Reads console lines on a background thread into a bounded queue; the main thread takes them with <see cref="TryTake"/>.
/// The thread only reads and enqueues. <c>ReadLine</c> cannot be interrupted, so nobody waits for the thread when the server stops.
/// </summary>
public sealed class ConsoleInputPump
{
    /// <summary>Capacity of the queue; lines beyond it are dropped with a warning.</summary>
    public const int QueueCapacity = 64;

    private readonly TextReader _input;
    private readonly ILogger<ConsoleInputPump> _logger;
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _dropped;
    private int _started;

    /// <summary>Initializes a new instance of the <see cref="ConsoleInputPump"/> class.</summary>
    /// <param name="input">The standard input (a <see cref="StringReader"/> in tests).</param>
    /// <param name="logger">Logger.</param>
    public ConsoleInputPump(TextReader input, ILogger<ConsoleInputPump> logger)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(logger);
        _input = input;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the input ended (end of file, or a read error); the server keeps running.</summary>
    public bool HasEnded => _ended.Task.IsCompleted;

    /// <summary>Gets the number of lines dropped because the queue was full or the line was too long.</summary>
    public int DroppedLines => Volatile.Read(ref _dropped);

    /// <summary>Starts the reading thread; once.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        new Thread(Run) { IsBackground = true, Name = "Horyzont console input" }.Start();
    }

    /// <summary>Takes the oldest queued line; main thread.</summary>
    /// <param name="line">The line.</param>
    /// <returns>True when a line was taken.</returns>
    public bool TryTake(out string line) => _queue.Reader.TryRead(out line!);

    /// <summary>Waits until the input ended (tests).</summary>
    /// <param name="timeout">Longest wait.</param>
    /// <returns>True when the input ended.</returns>
    public bool WaitForEnd(TimeSpan timeout) => _ended.Task.Wait(timeout);

    private void Run()
    {
        try
        {
            while (_input.ReadLine() is { } line)
            {
                if (line.Length > ConsoleCommandProcessor.MaxLineLength)
                {
                    Drop("line longer than 256 characters");
                }
                else if (!_queue.Writer.TryWrite(line))
                {
                    Drop("the queue of 64 lines is full");
                }
            }

            _logger.ConsoleInputEnded();
        }
#pragma warning disable CA1031 // The console is optional: a failing standard input ends the pump and the server keeps running; the error is logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.ConsoleReadFailed(ex);
        }
        finally
        {
            _ended.TrySetResult();
        }
    }

    private void Drop(string reason)
    {
        Interlocked.Increment(ref _dropped);
        _logger.ConsoleInputDropped(reason);
    }
}
