using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Frames;

namespace Horyzont.Server.Space;

/// <summary>
/// The default <see cref="IShipDemandSource"/> of K5-22 (decision 2b): a ship demands terrain when its world pose recorded by
/// <see cref="ServerFrameHistory"/> moved more than <see cref="ThresholdMeters"/> over the last <see cref="WindowTicks"/> ticks
/// (a flying ship starts loading when it enters the zone — spec 02:207). A ship recorded for less than the window compares with the
/// previous tick. K5-24 replaces it with <c>UnparkedShipDemand</c>.
/// </summary>
public sealed class MovingShipDemand : IShipDemandSource
{
    /// <summary>Displacement over the window that counts as moving, in metres.</summary>
    public const double ThresholdMeters = 0.5;

    /// <summary>Length of the window in ticks.</summary>
    public const int WindowTicks = 30;

    private readonly ServerFrameHistory _history;

    /// <summary>Initializes a new instance of the <see cref="MovingShipDemand"/> class.</summary>
    /// <param name="history">The server frame history (records ship poses in phase Compose).</param>
    public MovingShipDemand(ServerFrameHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
    }

    /// <inheritdoc />
    public bool Demands(Construct ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        if (!_history.HasTick || ship.Mode != ConstructMode.Ship)
        {
            return false;
        }

        var frame = FrameRef.Of(ship.Id);
        var last = _history.LastTick.Value;
        var view = _history.History;
        if (!view.TryGetWorldPose(frame, last, out var now))
        {
            return false;
        }

        if ((last >= WindowTicks && view.TryGetWorldPose(frame, last - WindowTicks, out var before))
            || (last >= 1 && view.TryGetWorldPose(frame, last - 1, out before)))
        {
            return Vector3D.Distance(now.Position, before.Position) > ThresholdMeters;
        }

        return false;
    }
}
