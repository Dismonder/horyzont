using System.Globalization;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Server.Tests.Ships;

/// <summary>
/// In-flight measures of the K5-20/K5-26 plans, client against server at the render tick of every frame: the world pose error of a
/// ship (and of a passenger composed with its local pose) and the jump, the client displacement in one frame beyond the fastest
/// server displacement around it. The error of every render tick bracketed by two consecutive snapshots (the normal interval of
/// <see cref="SnapshotInterval"/> ticks) is measured. Only a render tick after a lost snapshot (a wider bracket, or extrapolation
/// past the last snapshot) whose server path is not a straight line within <see cref="ChordTolerance"/> (the flight model changes
/// speed within the interval, e.g. the pulse drive) cannot be reproduced by any interpolation of the received samples: its error is
/// counted apart (<see cref="Nonlinear"/>, <see cref="MaxNonlinearError"/>), its jump is still checked. The bracketing ticks are
/// those of the snapshots the client applied for the ship (K5-26); while the client extrapolates the interval ends at the render tick.
/// </summary>
internal sealed class ShipPoseMeasure(ShipCoopRig rig)
{
    /// <summary>Distance of the server path from the chord between snapshot ticks above which a frame is not interpolable.</summary>
    public const double ChordTolerance = 0.25;

    /// <summary>Ticks between two consecutive snapshots of a ship (20 Hz at 60 Hz).</summary>
    public const int SnapshotInterval = 3;

    private readonly Dictionary<(ShipCoopClient, EntityId), (Vector3D Point, long Tick)> _last = [];
    private readonly List<double> _errors = [];

    public double MaxError { get; private set; }

    public double MaxJump { get; private set; }

    public string WorstJump { get; private set; } = "";

    public string WorstError { get; private set; } = "";

    /// <summary>Gets the render frames compared, measured and <see cref="Nonlinear"/> ones together.</summary>
    public int Samples => _errors.Count + Nonlinear;

    public int Missing { get; private set; }

    public int Nonlinear { get; private set; }

    public double MaxNonlinearError { get; private set; }

    /// <summary>Measures one ship as one client sees it, with an optional local pose (a passenger) composed on both sides.</summary>
    public void Sample(ShipCoopClient client, EntityId ship, PoseD? serverLocal = null, PoseD? clientLocal = null)
    {
        var tick = rig.ServerTick - ShipCoopRig.RenderDelayTicks;
        var frame = FrameRef.Of(ship);
        if (!rig.ServerHistory.TryGetWorldPose(frame, tick, out var server))
        {
            return;
        }

        if (!client.Frames.TryGetWorldPose(frame, tick, out var seen))
        {
            Missing++;
            _last.Remove((client, ship));
            return;
        }

        var serverPoint = serverLocal is { } sl ? FrameMath.Compose(server, sl).Position : server.Position;
        var clientPoint = clientLocal is { } cl ? FrameMath.Compose(seen, cl).Position : seen.Position;
        var error = Vector3D.Distance(serverPoint, clientPoint);
        if (!SpansLostSnapshot(client, ship, tick) || IsLinear(client, ship, tick, serverLocal))
        {
            _errors.Add(error);
            if (error > MaxError)
            {
                MaxError = error;
                WorstError = string.Create(CultureInfo.InvariantCulture,
                    $"tick {tick} {client.Client.Name} ship {ship.Value} error {error:F2} chord9 {Chord(frame, tick, 6):F2} accel {Accel(frame, tick):F0} m/s²");
            }
        }
        else
        {
            Nonlinear++;
            MaxNonlinearError = Math.Max(MaxNonlinearError, error);
        }

        if (_last.TryGetValue((client, ship), out var previous) && previous.Tick == tick - 1)
        {
            var last = previous.Point;
            var allowed = 0.0;
            for (var k = tick - 3; k <= tick + 3; k++)
            {
                if (TryServerPoint(frame, k, serverLocal, out var a) && TryServerPoint(frame, k - 1, serverLocal, out var b))
                {
                    allowed = Math.Max(allowed, Vector3D.Distance(a, b));
                }
            }

            var excess = Vector3D.Distance(clientPoint, last) - allowed;
            if (excess > MaxJump)
            {
                MaxJump = excess;
                WorstJump = string.Create(CultureInfo.InvariantCulture,
                    $"tick {tick} {client.Client.Name} ship {ship.Value} moved {Vector3D.Distance(clientPoint, last):F2} allowed {allowed:F2} error {error:F2} linear {IsLinear(client, ship, tick, serverLocal)}");
            }
        }

        _last[(client, ship)] = (clientPoint, tick);
    }

    public void Reset(ShipCoopClient client, EntityId ship) => _last.Remove((client, ship));

    public string Report()
    {
        var sorted = _errors.OrderBy(e => e).ToArray();
        var p99 = sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(sorted.Length * 0.99) - 1)];
        return string.Create(CultureInfo.InvariantCulture,
            $"samples={Samples} missing={Missing} maxError={MaxError:F4} m p99={p99:F4} m maxJumpExcess={MaxJump:F4} m nonlinear={Nonlinear} (max error {MaxNonlinearError:F2} m); worst jump: {WorstJump}; worst error: {WorstError}");
    }

    private bool TryServerPoint(FrameRef frame, long tick, PoseD? local, out Vector3D point)
    {
        point = default;
        if (!rig.ServerHistory.TryGetWorldPose(frame, tick, out var pose))
        {
            return false;
        }

        point = local is { } l ? FrameMath.Compose(pose, l).Position : pose.Position;
        return true;
    }

    private double Chord(FrameRef frame, long tick, int half)
    {
        if (!TryServerPoint(frame, tick - half, null, out var a) || !TryServerPoint(frame, tick + half, null, out var b) || !TryServerPoint(frame, tick, null, out var p))
        {
            return double.NaN;
        }

        return Vector3D.Distance(p, Vector3D.Lerp(a, b, 0.5));
    }

    private double Accel(FrameRef frame, long tick)
    {
        if (!TryServerPoint(frame, tick - 1, null, out var a) || !TryServerPoint(frame, tick + 1, null, out var b) || !TryServerPoint(frame, tick, null, out var p))
        {
            return double.NaN;
        }

        return (a + b - p - p).Length() * 3600;
    }

    private static bool SpansLostSnapshot(ShipCoopClient client, EntityId ship, long tick) =>
        !client.SnapshotTicks.TryGetBracket(ship, tick, out var before, out var after) || after is not { } end
        || end - before > SnapshotInterval;

    private bool IsLinear(ShipCoopClient client, EntityId ship, long tick, PoseD? local)
    {
        var frame = FrameRef.Of(ship);
        long start;
        long end;
        if (client.SnapshotTicks.TryGetBracket(ship, tick, out var before, out var after))
        {
            start = before;
            end = after ?? tick;
        }
        else
        {
            start = tick - (tick % 3);
            end = start + 3;
        }

        if (end <= start)
        {
            return true;
        }

        if (!TryServerPoint(frame, start, local, out var a) || !TryServerPoint(frame, end, local, out var b)
            || !TryServerPoint(frame, tick, local, out var p))
        {
            return false;
        }

        var t = (tick - start) / (double)(end - start);
        return Vector3D.Distance(p, Vector3D.Lerp(a, b, t)) <= ChordTolerance;
    }
}
