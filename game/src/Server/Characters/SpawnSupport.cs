using System.Numerics;

using Horyzont.Game.Physics;

namespace Horyzont.Game.Server.Characters;

/// <summary>Readiness and placement decisions for a saved character pose.</summary>
internal static class SpawnSupport
{
    internal const uint CollisionMask = CollisionLayers.TerrainMask | CollisionLayers.ConstructsMask;

    internal static bool CanSettle(bool terrainReady, bool constructsReady) => terrainReady && constructsReady;

    internal static bool CanExpire(bool constructsReady) => constructsReady;

    internal static Vector3 FeetOnSupport(Vector3 savedFeet, Vector3 support, Vector3 up, float clearance)
    {
        var above = Vector3.Dot(savedFeet - support, up);
        return above >= 0 && above <= clearance ? savedFeet : support + (up * clearance);
    }
}
