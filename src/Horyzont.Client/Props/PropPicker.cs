using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Props;
using Horyzont.Core.Simulation;

namespace Horyzont.Client.Props;

/// <summary>
/// Finds the prop under the crosshair (K6-31 D19): the nearest prop, not harvested, whose stand-in body (<see cref="PropShapesV1"/>, the
/// capsule the validator measures too) the eye ray enters within the reach of the client and strictly before it hits the terrain (a tie
/// goes to the terrain, like the constructs of <see cref="ToolTargeting"/>). Keeps its scratch list: steady state allocates nothing.
/// </summary>
/// <param name="source">Where the props around the eye come from.</param>
/// <param name="states">The harvested props.</param>
public sealed class PropPicker(IPropSource source, IPropStateView states)
{
    // The anchor of a prop can lie this far (tallest tree at the largest scale) from the point where the ray touches it.
    private const float AnchorSearchMarginMeters = 32f;

    private readonly IPropSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IPropStateView _states = states ?? throw new ArgumentNullException(nameof(states));
    private readonly List<PropInstance> _candidates = [];

    /// <summary>Finds the nearest prop whose body the ray enters before the terrain, within the reach of the client.</summary>
    /// <param name="aim">The aim.</param>
    /// <param name="world">World view (terrain).</param>
    /// <param name="rules">Game rules (reach).</param>
    /// <param name="pick">The prop.</param>
    /// <returns>True when there is one.</returns>
    public bool TryPick(in AimRay aim, IToolWorldView world, GameRules rules, out PropPick pick)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rules);
        pick = default;
        var reach = ToolTargeting.ClientReach(rules);
        var limit = world.RaycastTerrain(aim.Planet, aim.Eye, aim.Direction, reach, out var terrain) && terrain.Distance <= reach ? terrain.Distance : reach;
        _candidates.Clear();
        _source.CollectInSphere(aim.Planet, aim.Eye, reach + AnchorSearchMarginMeters, _candidates);
        var best = limit;
        var found = false;
        foreach (var prop in _candidates)
        {
            if (_states.IsHarvested(aim.Planet, prop.Id) || !TryEnter(aim.Eye, aim.Direction, prop, out var distance))
            {
                continue;
            }

            // Strictly before the terrain and within reach; among props the nearest one.
            if (distance < best)
            {
                best = distance;
                pick = new PropPick(aim.Planet, prop, distance);
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Distance along a ray to the body of a prop: the closest approach of the ray to the segment of the trunk, pulled back by the part of the
    /// radius that lies on the way (exact for a ray across the axis, a little short for a slanting one, which only helps the player).
    /// </summary>
    private static bool TryEnter(Vector3 origin, Vector3 direction, PropInstance prop, out float distance)
    {
        distance = 0f;
        var radius = prop.BodyRadiusMeters;
        var axis = prop.Up * prop.TrunkHeightMeters;
        var e = axis.LengthSquared();
        var r = origin - prop.Anchor;

        // Closest points of the ray (origin + t * direction, t >= 0) and the segment (anchor + s * axis, s in [0, 1]), for a unit direction.
        var f = Vector3.Dot(axis, r);
        var b = Vector3.Dot(direction, axis);
        var c = Vector3.Dot(direction, r);
        float t;
        float s;
        if (e < 1e-8f)
        {
            s = 0f;
            t = Math.Max(-c, 0f);
        }
        else
        {
            var denominator = e - (b * b);
            t = denominator > 1e-8f ? Math.Max(((b * f) - (c * e)) / denominator, 0f) : 0f;
            s = (b * t + f) / e;
            if (s < 0f)
            {
                s = 0f;
                t = Math.Max(-c, 0f);
            }
            else if (s > 1f)
            {
                s = 1f;
                t = Math.Max(b - c, 0f);
            }
        }

        var gap = Vector3.Distance(origin + (direction * t), prop.Anchor + (axis * s));
        if (gap > radius)
        {
            return false;
        }

        distance = Math.Max(0f, t - MathF.Sqrt((radius * radius) - (gap * gap)));
        return true;
    }
}
