using System.Globalization;
using System.Text;

namespace Horyzont.Game.Logging;

/// <summary>
/// A text log file that rotates by size: <c>name.log</c> is the current file, <c>name.1.log</c> the previous one and so on up to
/// <c>name.&lt;files-1&gt;.log</c>; the oldest is deleted. Thread-safe (log calls come from the main thread and from workers).
/// </summary>
public sealed class RotatingLogFile : IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _name;
    private readonly long _maxBytes;
    private readonly int _files;
    private StreamWriter? _writer;
    private long _size;
    private bool _disposed;

    /// <summary>Opens (appends to) the current log file.</summary>
    /// <param name="directory">Directory of the logs; created when missing.</param>
    /// <param name="name">Base name without extension.</param>
    /// <param name="maxBytes">Size at which the file rotates.</param>
    /// <param name="files">Number of files kept, the current one included (at least 1).</param>
    public RotatingLogFile(string directory, string name, long maxBytes, int files)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1024L);
        ArgumentOutOfRangeException.ThrowIfLessThan(files, 1);
        _directory = directory;
        _name = name;
        _maxBytes = maxBytes;
        _files = files;
        Directory.CreateDirectory(directory);
        Open();
    }

    /// <summary>Path of the current file.</summary>
    public string CurrentPath => PathOf(0);

    /// <summary>Appends one line (a newline is added) and flushes it.</summary>
    /// <param name="line">Text without a trailing newline.</param>
    public void WriteLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_size >= _maxBytes)
            {
                Rotate();
            }

            _writer!.WriteLine(line);
            _writer.Flush();
            _size += Encoding.UTF8.GetByteCount(line) + 1;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private string PathOf(int index) =>
        Path.Combine(_directory, index == 0 ? $"{_name}.log" : string.Create(CultureInfo.InvariantCulture, $"{_name}.{index}.log"));

    private void Open()
    {
        var path = PathOf(0);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _size = stream.Length;
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
    }

    private void Rotate()
    {
        _writer!.Dispose();
        _writer = null;
        if (_files == 1)
        {
            File.Delete(PathOf(0));
        }
        else
        {
            File.Delete(PathOf(_files - 1));
            for (var i = _files - 2; i >= 0; i--)
            {
                if (File.Exists(PathOf(i)))
                {
                    File.Move(PathOf(i), PathOf(i + 1), overwrite: true);
                }
            }
        }

        Open();
    }
}
