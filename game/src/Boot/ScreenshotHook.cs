using Godot;

namespace Horyzont.Game.Boot;

/// <summary>
/// Development aid (<c>--screenshot &lt;file.png&gt; [--screenshot-after seconds]</c>): saves the game window once, then asks for the
/// normal quit, which runs the canonical shutdown. Not available in a headless process (nothing is drawn there).
/// </summary>
public sealed partial class ScreenshotHook : Node
{
    private readonly string _path;
    private readonly double _afterSeconds;
    private readonly Action<string> _print;
    private double _elapsed;
    private bool _taken;

    /// <summary>Creates the hook.</summary>
    /// <param name="path">Absolute path of the PNG.</param>
    /// <param name="afterSeconds">Seconds after the node entered the tree.</param>
    /// <param name="print">Where the result line goes.</param>
    public ScreenshotHook(string path, double afterSeconds, Action<string> print)
    {
        _path = path;
        _afterSeconds = afterSeconds;
        _print = print;
        Name = "ScreenshotHook";
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (!_taken && _elapsed >= _afterSeconds)
        {
            _taken = true;
            _ = TakeAsync();
        }
    }

    private async Task TakeAsync()
    {
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var error = image.SavePng(_path);
            _print(error == Error.Ok ? $"SCREENSHOT saved {_path}" : $"SCREENSHOT failed {error}");
        }
#pragma warning disable CA1031 // A development aid: report the failure and still end the run through the normal quit.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _print($"SCREENSHOT failed {ex.Message}");
        }

        // The same path as the window's close button: the quit coordinator starts the canonical shutdown.
        GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
    }
}
