using Horyzont.Client.Markers;
using Horyzont.Client.Replication;
using Horyzont.Client.Space;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Space;

namespace Horyzont.Client.Navigation;

/// <summary>
/// What the system map draws (K5-27 decision 12), computed in the client library so it is testable without the engine: the bodies of
/// the star system, the direction of the sun, the entities the client knows (decision 13: only those inside the interest range) and the
/// markers, all in the system frame in double precision. The panel only draws it. Built when the map opens or something changed.
/// </summary>
public sealed class SystemMapModel
{
    private static readonly string[] _bodyKeys = ["BODY_HOME", "BODY_MOON", "BODY_B", "BODY_C"];

    private SystemMapModel(
        IReadOnlyList<SystemMapBody> bodies, Vector3D sunDirection, IReadOnlyList<SystemMapEntity> entities, IReadOnlyList<SystemMapMarker> markers,
        Vector3D? own)
    {
        Bodies = bodies;
        SunDirection = sunDirection;
        Entities = entities;
        Markers = markers;
        OwnPosition = own;
    }

    /// <summary>Gets the bodies in body order.</summary>
    public IReadOnlyList<SystemMapBody> Bodies { get; }

    /// <summary>Gets the unit direction of the sun, from <see cref="StarSystemMath.SunDirection"/>.</summary>
    public Vector3D SunDirection { get; }

    /// <summary>Gets the players and ships the client knows.</summary>
    public IReadOnlyList<SystemMapEntity> Entities { get; }

    /// <summary>Gets the markers in id order.</summary>
    public IReadOnlyList<SystemMapMarker> Markers { get; }

    /// <summary>Gets the own position in the system frame, when known.</summary>
    public Vector3D? OwnPosition { get; }

    /// <summary>Gets the localization key of the name of a body.</summary>
    /// <param name="bodyIndex">Index of the body.</param>
    /// <returns>The key.</returns>
    public static string BodyKey(byte bodyIndex) => bodyIndex < _bodyKeys.Length ? _bodyKeys[bodyIndex] : "BODY_" + bodyIndex;

    /// <summary>Builds the model.</summary>
    /// <param name="stars">The star system of the client; without a system the model has no bodies.</param>
    /// <param name="poses">World poses of frames.</param>
    /// <param name="markers">Markers of the client.</param>
    /// <param name="entities">Replicated entities; null when none.</param>
    /// <param name="viewpoint">Own position.</param>
    /// <param name="simulationTick">Server tick, for the sun.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <param name="renderTick">Server tick being rendered (fractional).</param>
    /// <param name="entityName">Name of an entity, or null; called for every known entity.</param>
    /// <returns>The model.</returns>
    public static SystemMapModel Build(
        ClientStarSystem stars, IWorldPoseSource poses, ClientMarkerStore markers, EntityMirror? entities, INavigationViewpoint viewpoint,
        ulong simulationTick, int tickRate, double renderTick, Func<EntityId, PlayerId?, EntityKind, string?>? entityName = null)
    {
        ArgumentNullException.ThrowIfNull(stars);
        ArgumentNullException.ThrowIfNull(poses);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(viewpoint);

        var bodies = new List<SystemMapBody>();
        if (stars.Model is { } model)
        {
            for (var i = 0; i < model.BodyCount; i++)
            {
                var body = model.GetBody(i);
                bodies.Add(new SystemMapBody(
                    body.Planet, body.BodyIndex, BodyKey(body.BodyIndex), body.Position.X, body.Position.Z, body.RadiusMeters,
                    StarSystemRules.ZoneRadiusFactor * body.RadiusMeters));
            }
        }

        var sun = stars.SunCycleSeconds > 0 ? StarSystemMath.SunDirection(simulationTick, tickRate, stars.SunCycleSeconds) : new Vector3D(1, 0, 0);
        var known = new List<SystemMapEntity>();
        if (entities is not null)
        {
            foreach (var id in entities.Ids)
            {
                if (!entities.TryGet(id, out var spawn) || spawn is null || !entities.TryGetPose(id, out var frame, out var state)
                    || !poses.TryGetWorldPose(frame.Frame, renderTick, out var pose))
                {
                    continue;
                }

                var world = FrameMath.TransformPoint(pose, Vector3D.FromFloat(state.Position));
                known.Add(new SystemMapEntity(id, spawn.Kind, spawn.Owner, entityName?.Invoke(id, spawn.Owner, spawn.Kind), world.X, world.Y, world.Z));
            }
        }

        var mapMarkers = new List<SystemMapMarker>();
        foreach (var marker in markers.All)
        {
            if (!poses.TryGetWorldPose(FrameRef.Of(marker.Planet), renderTick, out var planet))
            {
                continue;
            }

            var world = FrameMath.TransformPoint(planet, Vector3D.FromFloat(marker.Position));
            mapMarkers.Add(new SystemMapMarker(marker.Id, marker.Name, marker.Icon, marker.Color, marker.IsShared, marker.Planet, world.X, world.Y, world.Z));
        }

        Vector3D? own = viewpoint.TryGetWorldPosition(renderTick, out var position) ? position : null;
        return new SystemMapModel(bodies, sun, known, mapMarkers, own);
    }
}
