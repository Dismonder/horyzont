using System.Numerics;

using Horyzont.Game.Physics;

namespace Horyzont.Game.Server.Characters;

/// <summary>Readiness and placement decisions for a saved character pose.</summary>
internal static class SpawnSupport
{
    internal const uint CollisionMask = CollisionLayers.TerrainMask | CollisionLayers.ConstructsMask;

    internal static bool CanSettle(bool blocksReady, bool terrainCollisionReady, bool constructsReady) =>
        blocksReady && terrainCollisionReady && constructsReady;

    internal static bool CanExpire(int waitedTicks, int deadlineTicks) => waitedTicks >= deadlineTicks;

    internal static Vector3 FeetOnSupport(Vector3 savedFeet, Vector3 support, Vector3 up, float clearance)
    {
        var above = Vector3.Dot(savedFeet - support, up);
        return above >= 0 && above <= clearance ? savedFeet : support + (up * clearance);
    }
}
