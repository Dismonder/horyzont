using Horyzont.Bots.Scripting;
using Horyzont.Client.Tools;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Client.Tools;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Bots;

/// <summary>
/// The input of the bot mode (<c>--bot</c>, K4-19): the <see cref="BotScript"/> of the bots without an engine drives the real local
/// player through the input seams of K3-14, so movement goes through the prediction and the input batches of a player and the tools
/// through their pacing; the aim is the camera of the player. One command per script tick, taken from the shared epoch.
/// </summary>
public sealed class BotInputSource : ILocalInputSource, IToolInputSource
{
    private readonly BotScript _script;
    private readonly LocalPlayer _player;
    private readonly Func<long> _tick;
    private long _commandTick = long.MinValue;
    private long _toolTick = long.MinValue;
    private BotCommand _command = BotCommand.Idle(0f);
    private NumVector3 _home;
    private bool _hasHome;

    /// <summary>Creates the input.</summary>
    /// <param name="script">The script of the bot.</param>
    /// <param name="player">The local player it drives.</param>
    /// <param name="tick">The script tick now (ticks since the shared epoch; negative before it).</param>
    public BotInputSource(BotScript script, LocalPlayer player, Func<long> tick)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(tick);
        _script = script;
        _player = player;
        _tick = tick;
    }

    /// <summary>Gets the number of tool actions pressed.</summary>
    public long ToolActions { get; private set; }

    /// <inheritdoc />
    public LocalInputSample Take()
    {
        if (!_player.IsActive || !Current(out var command))
        {
            return LocalInputSample.None;
        }

        var up = TerrainMath.UpAt(_player.PredictedFeet);
        var (yaw, pitch) = BotControls.LookDelta(_player.BodyRotation, up, _player.Pitch, command.Yaw, command.Pitch);
        var buttons = (command.Jump ? InputButtons.Jump : InputButtons.None) | (command.Sprint ? InputButtons.Sprint : InputButtons.None);
        return new LocalInputSample(command.MoveX, command.MoveY, buttons, yaw, pitch);
    }

    /// <inheritdoc />
    ToolInputFrame IToolInputSource.Take()
    {
        if (!_player.IsActive || !Current(out var command) || _toolTick == _commandTick)
        {
            return default;
        }

        // A press is an edge: one frame per script tick, however often the tools ask.
        _toolTick = _commandTick;
        var frame = BotControls.FrameFor(command.Action);
        if (command.Action != BotAction.None)
        {
            ToolActions++;
        }

        return frame;
    }

    private bool Current(out BotCommand command)
    {
        var tick = _tick();
        if (tick < 0)
        {
            command = default;
            return false;
        }

        if (!_hasHome)
        {
            _home = _player.PredictedFeet;
            _hasHome = true;
        }

        if (tick != _commandTick)
        {
            // BotScript.At only moves forward; a tick that was skipped (a slow frame) is simply not played.
            _command = _script.At(tick, BotControls.SenseHome(_player.PredictedFeet, _home));
            _commandTick = tick;
        }

        command = _command;
        return true;
    }
}
