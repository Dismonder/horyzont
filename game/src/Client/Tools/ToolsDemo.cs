using Godot;

using Horyzont.Client.Tools;
using Horyzont.Game.Client.Character;

namespace Horyzont.Game.Client.Tools;

/// <summary>
/// Development aid (<c>--dev-tools-demo</c>): looks down at the ground and plays a short script on the tools once they aim at the
/// terrain (dig, fill with dirt, then three blocks of a small construct), so a screenshot shows real edits and the ghost. It replaces
/// the tool input; the keyboard still moves the character.
/// </summary>
public sealed partial class ToolsDemo : Node, IToolInputSource, ILocalInputSource
{
    private const float LookDownRadians = -0.6f;

    private static readonly (int Tick, ToolInputFrame Frame)[] _script =
    [
        (0, Frame(primaryPressed: true, radiusSteps: 4)),
        (40, Frame(secondaryPressed: true, materialSteps: 1, radiusSteps: -4)),
        (80, Frame(select: ToolKind.Builder)),
        (95, Frame(secondaryPressed: true)),
        (120, Frame(yawSteps: 1)),
        (135, Frame(secondaryPressed: true)),
        (160, Frame(upSteps: 1)),
        (175, Frame(secondaryPressed: true)),
    ];

    private readonly ToolNode _tools;
    private readonly ILocalInputSource _player;
    private bool _pitched;
    private long _start = -1;
    private int _next;

    /// <summary>Creates the demo.</summary>
    /// <param name="tools">The tools to drive.</param>
    /// <param name="player">The ordinary input of the character, which keeps working.</param>
    public ToolsDemo(ToolNode tools, ILocalInputSource player)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(player);
        _tools = tools;
        _player = player;
        Name = "ToolsDemo";
    }

    /// <inheritdoc />
    LocalInputSample ILocalInputSource.Take()
    {
        var sample = _player.Take();
        if (_pitched)
        {
            return sample;
        }

        _pitched = true;
        return sample with { MousePitch = sample.MousePitch + LookDownRadians };
    }

    /// <inheritdoc />
    public ToolInputFrame Take()
    {
        if (_start < 0)
        {
            if (_tools.Preview.Kind == ToolPreviewKind.None)
            {
                return default;
            }

            _start = _tools.Ticks;
        }

        if (_next < _script.Length && _tools.Ticks - _start >= _script[_next].Tick)
        {
            return _script[_next++].Frame;
        }

        return default;
    }

    private static ToolInputFrame Frame(
        bool primaryPressed = false, bool secondaryPressed = false, ToolKind? select = null, int radiusSteps = 0, int materialSteps = 0, int yawSteps = 0,
        int upSteps = 0) =>
        new(primaryPressed, false, secondaryPressed, false, select, radiusSteps, 0, materialSteps, yawSteps, upSteps);
}
