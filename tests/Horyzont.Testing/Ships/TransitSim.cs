using Horyzont.Core.Frames;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Space;
using Horyzont.Server.Ships;

namespace Horyzont.Testing.Ships;

/// <summary>Result of a <see cref="TransitSim"/> flight.</summary>
/// <param name="Parked">True when the ship parked on the target.</param>
/// <param name="Seconds">Seconds from the release of the gear to parking (or the timeout).</param>
/// <param name="FuelUsed">Fuel burnt.</param>
/// <param name="MaxSpeed">Highest speed, m/s.</param>
/// <param name="Hits">Collider hits.</param>
/// <param name="Timeline">Phase changes: phase, seconds, ground distance, speed.</param>
public readonly record struct TransitResult(bool Parked, double Seconds, double FuelUsed, double MaxSpeed, int Hits, string Timeline);

/// <summary>
/// Flies a ship from the surface of the home planet to a body of a star system with the flight model, the
/// <see cref="DevTransitPilot"/> and a <see cref="SphereGroundCollider"/> (K5-24 criterion 1). Positions are kept in the system
/// frame; the environment is the planet frame of a body inside its exit sphere (3.2 R) and the system frame elsewhere — the frames
/// of the early access have the same axes, so the velocity is the same in both.
/// </summary>
public static class TransitSim
{
    /// <summary>Flies one transit.</summary>
    /// <param name="generated">The generated star system.</param>
    /// <param name="target">Body index of the target.</param>
    /// <param name="stats">Ship stats.</param>
    /// <param name="rules">Rules.</param>
    /// <param name="usePulse">False flies without the pulse drive.</param>
    /// <param name="maxSeconds">Timeout.</param>
    /// <returns>The result.</returns>
    public static TransitResult Fly(GeneratedStarSystem generated, int target, ShipStats stats, FlightRules rules, bool usePulse, double maxSeconds)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(stats);
        const float dt = 1f / 60f;
        var model = new StarSystemModel(generated.System, generated.Bodies);
        var collider = new SphereGroundCollider();
        for (var i = 0; i < model.BodyCount; i++)
        {
            collider.Add(model.GetBody(i).Position, model.GetBody(i).RadiusMeters);
        }

        var home = model.GetBody(0);
        var goal = model.GetBody(target);
        var up = Vector3D.Normalize(goal.Position - home.Position);
        var rotation = FromTo(new Vector3D(0, 1, 0), up);
        var com = Vector3D.FromFloat(stats.CenterOfMass);
        var comPosition = home.Position + (up * (home.RadiusMeters + stats.BoundingRadius + 0.01));
        var motion = new ShipMotion
        {
            Pose = new PoseD(comPosition - rotation.Rotate(com), rotation),
            Fuel = stats.FuelCapacity,
            Flags = ShipFlightFlags.Parked | ShipFlightFlags.GearDown,
        };
        var pilot = new DevTransitPilot(usePulse);
        var obstacles = new BodySphere[model.BodyCount];
        for (var i = 0; i < obstacles.Length; i++)
        {
            obstacles[i] = new BodySphere(model.GetBody(i).Position, model.GetBody(i).RadiusMeters);
        }

        var maxSpeed = 0.0;
        var timeline = new System.Text.StringBuilder();
        var phase = pilot.Phase;
        var steps = (int)(maxSeconds / dt);
        for (var i = 0; i < steps; i++)
        {
            var e = Environment(model, generated, motion.Pose.Position + motion.Pose.Rotation.Rotate(com), rules);
            var ground = collider.GroundDistance(motion.Pose, stats, e.Up, 5000);
            var control = pilot.Compute(motion, stats, e, goal.Position, goal.RadiusMeters, ground, rules, obstacles);
            if (pilot.Phase != phase)
            {
                phase = pilot.Phase;
                timeline.Append(System.Globalization.CultureInfo.InvariantCulture, $"{phase}@{i * (double)dt:F1}s(g={ground:F0},v={motion.LinearVelocity.Length():F0}) ");
            }
            if (pilot.Phase == DevTransitPhase.Arrived)
            {
                return new TransitResult(true, i * (double)dt, stats.FuelCapacity - motion.Fuel, maxSpeed, collider.Hits, timeline.ToString());
            }

            var from = motion.Pose;
            ShipFlightModel.Step(ref motion, control, stats, e, rules, dt);
            ShipContact.Resolve(ref motion, from, collider.Sweep(from, motion.Pose, stats), e, rules);
            maxSpeed = Math.Max(maxSpeed, motion.LinearVelocity.Length());
        }

        return new TransitResult(false, maxSeconds, stats.FuelCapacity - motion.Fuel, maxSpeed, collider.Hits, timeline.ToString());
    }

    /// <summary>The environment of a point given in the system frame: the planet frame inside an exit sphere, else the system frame.</summary>
    /// <param name="model">The system.</param>
    /// <param name="generated">The generated system (planets in body order).</param>
    /// <param name="point">Point in the system frame.</param>
    /// <param name="rules">Rules.</param>
    /// <returns>The environment, vectors in the (same) axes of both frames.</returns>
    public static FlightEnvironment Environment(StarSystemModel model, GeneratedStarSystem generated, in Vector3D point, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(generated);
        for (var i = 0; i < model.BodyCount; i++)
        {
            var body = model.GetBody(i);
            if (Vector3D.Distance(point, body.Position) <= StarSystemRules.ZoneExitFactor * body.RadiusMeters)
            {
                return FlightEnvironments.InPlanet(generated.Bodies[i], i, point - body.Position, rules);
            }
        }

        return FlightEnvironments.InSystem(model, point, rules);
    }

    private static QuaternionD FromTo(in Vector3D a, in Vector3D b)
    {
        var axis = Vector3D.Cross(a, b);
        var length = axis.Length();
        var cosine = Math.Clamp(Vector3D.Dot(a, b), -1, 1);
        return length < 1e-12 ? QuaternionD.Identity : QuaternionD.FromAxisAngle(axis / length, Math.Acos(cosine));
    }
}
