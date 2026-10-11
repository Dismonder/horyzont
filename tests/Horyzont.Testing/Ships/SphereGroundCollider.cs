using Horyzont.Core.Frames;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Testing.Ships;

/// <summary>
/// Test collider (K5-24 decision 9): every body is a ball of radius R (plus an optional height function); the ship is its
/// bounding sphere about the centre of mass, swept continuously along the segment of the step.
/// </summary>
public sealed class SphereGroundCollider
{
    private readonly List<(Vector3D Center, double Radius)> _bodies = [];
    private readonly Func<Vector3D, double>? _height;

    /// <summary>Initializes a new instance of the <see cref="SphereGroundCollider"/> class.</summary>
    /// <param name="height">Optional ground height over R for a unit direction from the body centre.</param>
    public SphereGroundCollider(Func<Vector3D, double>? height = null) => _height = height;

    /// <summary>Gets the number of sweeps that hit.</summary>
    public int Hits { get; private set; }

    /// <summary>Adds a body.</summary>
    /// <param name="center">Centre in the frame of the sweeps.</param>
    /// <param name="radius">Radius R.</param>
    /// <returns>This collider.</returns>
    public SphereGroundCollider Add(in Vector3D center, double radius)
    {
        _bodies.Add((center, radius));
        return this;
    }

    /// <summary>Sweeps the ship from one pose to another.</summary>
    /// <param name="from">Start pose.</param>
    /// <param name="to">End pose.</param>
    /// <param name="stats">Ship stats (centre of mass, bounding radius).</param>
    /// <returns>The result.</returns>
    public ShipSweepResult Sweep(in PoseD from, in PoseD to, ShipStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        var com = Vector3D.FromFloat(stats.CenterOfMass);
        var a = from.Position + from.Rotation.Rotate(com);
        var b = to.Position + to.Rotation.Rotate(com);
        var best = ShipSweepResult.Free;
        foreach (var (center, radius) in _bodies)
        {
            var reach = Ground(center, radius, b) + stats.BoundingRadius;
            var hit = Segment(a, b, center, reach);
            if (hit is { } t && t < best.Fraction + (best.Hit ? 0 : 1e-12))
            {
                var point = a + ((b - a) * t);
                best = new ShipSweepResult(true, Math.Max(0, t - 1e-6), Vector3D.Normalize(point - center));
            }
        }

        if (best.Hit)
        {
            Hits++;
        }

        return best;
    }

    /// <summary>Distance from the ship down to the ground of the nearest body below it.</summary>
    /// <param name="pose">The ship pose.</param>
    /// <param name="stats">Ship stats.</param>
    /// <param name="up">Unit up direction.</param>
    /// <param name="probe">Probe length.</param>
    /// <returns>The distance; infinite when no ground within the probe.</returns>
    public double GroundDistance(in PoseD pose, ShipStats stats, in Vector3D up, double probe)
    {
        var down = new PoseD(pose.Position - (up * probe), pose.Rotation);
        var hit = Sweep(pose, down, stats);
        if (hit.Hit)
        {
            Hits--;
        }

        return hit.Hit ? hit.Fraction * probe : double.PositiveInfinity;
    }

    private double Ground(in Vector3D center, double radius, in Vector3D near)
    {
        if (_height is null)
        {
            return radius;
        }

        var offset = near - center;
        var length = offset.Length();
        return radius + (length > 0 ? _height(offset / length) : 0);
    }

    private static double? Segment(in Vector3D a, in Vector3D b, in Vector3D center, double reach)
    {
        var d = b - a;
        var f = a - center;
        var c = Vector3D.Dot(f, f) - (reach * reach);
        if (c <= 0)
        {
            return 0;
        }

        var aa = Vector3D.Dot(d, d);
        if (aa <= 0)
        {
            return null;
        }

        var bb = 2 * Vector3D.Dot(f, d);
        var disc = (bb * bb) - (4 * aa * c);
        if (disc < 0)
        {
            return null;
        }

        var t = (-bb - Math.Sqrt(disc)) / (2 * aa);
        return t is >= 0 and <= 1 ? t : null;
    }
}
