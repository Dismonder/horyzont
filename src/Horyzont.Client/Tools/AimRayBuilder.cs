using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Tools;

/// <summary>
/// Builds the aim ray of the tools. The ray always starts at the eye computed with the server formula, so the server accepts its
/// origin (<c>RayOriginMismatch</c> would answer a ray from the third person camera), and it points at what is under the crosshair.
/// </summary>
public static class AimRayBuilder
{
    private const float MinDirectionLength = 1e-3f;

    /// <summary>A camera closer to the eye than this is the first person camera.</summary>
    private const float FirstPersonDistance = 0.05f;

    /// <summary>The eye: feet + <see cref="TerrainMath.UpAt"/> · EyeHeight, the server formula (<c>IntentValidator.TryGetEye</c>).</summary>
    /// <param name="feet">Feet position in the planet frame.</param>
    /// <param name="rules">Game rules.</param>
    /// <returns>The eye position.</returns>
    public static Vector3 EyeFromFeet(Vector3 feet, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return feet + (TerrainMath.UpAt(feet) * rules.EyeHeight);
    }

    /// <summary>
    /// First person: the camera ray is the eye ray. Third person: the aim point is the first hit of the camera ray (terrain or
    /// construct) within |camera − eye| + <see cref="GameRules.ReachMeters"/>, otherwise the point at that distance; the ray goes from
    /// the eye to that point.
    /// </summary>
    /// <param name="planet">Planet of the frame.</param>
    /// <param name="feet">Predicted feet of the local player.</param>
    /// <param name="cameraOrigin">Camera position.</param>
    /// <param name="cameraDirection">Camera direction (any non-zero length).</param>
    /// <param name="bodyForward">Direction the body looks to; zero falls back to the camera direction.</param>
    /// <param name="world">World for the camera ray.</param>
    /// <param name="rules">Game rules.</param>
    /// <param name="ray">The aim.</param>
    /// <returns>False for non-finite input or a degenerate direction: nothing is sent then.</returns>
    public static bool TryBuild(
        PlanetId planet, Vector3 feet, Vector3 cameraOrigin, Vector3 cameraDirection, Vector3 bodyForward, IToolWorldView world, GameRules rules,
        out AimRay ray)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rules);
        ray = default;
        if (!IsFinite(feet) || !IsFinite(cameraOrigin) || !IsFinite(cameraDirection) || !IsFinite(bodyForward))
        {
            return false;
        }

        var cameraLength = cameraDirection.Length();
        if (cameraLength < MinDirectionLength)
        {
            return false;
        }

        var camera = cameraDirection / cameraLength;
        var look = bodyForward.Length() >= MinDirectionLength ? Vector3.Normalize(bodyForward) : camera;
        var eye = EyeFromFeet(feet, rules);
        var cameraDistance = Vector3.Distance(cameraOrigin, eye);
        if (cameraDistance < FirstPersonDistance)
        {
            ray = new AimRay(planet, eye, camera, look, feet);
            return true;
        }

        var maxDistance = cameraDistance + rules.ReachMeters;
        var point = cameraOrigin + (camera * maxDistance);
        var nearest = maxDistance;
        if (world.RaycastTerrain(planet, cameraOrigin, camera, maxDistance, out var terrain))
        {
            point = terrain.Position;
            nearest = terrain.Distance;
        }

        if (world.RaycastConstructs(planet, cameraOrigin, camera, nearest, out var construct) && construct.Distance < nearest)
        {
            point = construct.Position;
        }

        var toPoint = point - eye;
        var length = toPoint.Length();
        if (!float.IsFinite(length) || length < MinDirectionLength)
        {
            return false;
        }

        ray = new AimRay(planet, eye, toPoint / length, look, feet);
        return true;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
