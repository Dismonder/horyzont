using Godot;

using Horyzont.Core.World.Terrain;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Terrain;

/// <summary>
/// The terrain ray of the game: a ray march over the SDF with trilinear samples, stepping by the sampled distance (sphere tracing,
/// scaled by <see cref="TerrainEditSemantics.RaycastSdfStepFactor"/>, at least <see cref="TerrainEditSemantics.RaycastMinStepMeters"/>,
/// at most <see cref="TerrainEditSemantics.RaycastMaxSteps"/> steps), a binary search after the sign change and the normal from the SDF
/// gradient. The server terrain service and the terrain of a remote client share it. Main thread; allocates no managed memory.
/// </summary>
internal static class TerrainRaycaster
{
    /// <summary>Finds the nearest hit of a ray in the terrain a tool reads.</summary>
    /// <param name="tool">The voxel tool of the planet terrain.</param>
    /// <param name="origin">Ray origin, planet frame.</param>
    /// <param name="direction">Ray direction (any length).</param>
    /// <param name="maxDistance">Largest distance in metres.</param>
    /// <param name="hit">The hit when found.</param>
    /// <returns>Whether the ray hit the terrain.</returns>
    internal static bool Raycast(VoxelTool tool, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit)
    {
        hit = default;
        if (!float.IsFinite(maxDistance) || maxDistance <= 0f
            || !float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z)
            || !float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z) || direction == Vector3.Zero)
        {
            return false;
        }

        var o = TerrainVectors.ToGodot(origin);
        var d = TerrainVectors.ToGodot(direction).Normalized();
        double near = 0;
        var sdfNear = Sample(tool, o);
        double far;
        double sdfFar;
        if (sdfNear < 0)
        {
            far = 0;
            sdfFar = sdfNear;
        }
        else
        {
            var steps = 0;
            while (true)
            {
                if (++steps > TerrainEditSemantics.RaycastMaxSteps)
                {
                    return false;
                }

                // The sampled SDF is about the distance to the surface (in voxels of 1 m, divided by the edit SDF scale); the factor
                // keeps the step short of it where the trilinear field is steeper than 1, the minimum step bounds the work.
                var step = Math.Max(
                    sdfNear * TerrainEditSemantics.RaycastSdfStepFactor / TerrainEditSemantics.SdfScale, TerrainEditSemantics.RaycastMinStepMeters);
                far = Math.Min(near + step, maxDistance);
                sdfFar = Sample(tool, o + (d * far));
                if (sdfFar < 0)
                {
                    break;
                }

                if (far >= maxDistance)
                {
                    return false;
                }

                near = far;
                sdfNear = sdfFar;
            }

            for (var i = 0; i < TerrainEditSemantics.RaycastBinarySearchIterations; i++)
            {
                var mid = 0.5 * (near + far);
                var sdfMid = Sample(tool, o + (d * mid));
                if (sdfMid < 0)
                {
                    far = mid;
                    sdfFar = sdfMid;
                }
                else
                {
                    near = mid;
                    sdfNear = sdfMid;
                }
            }
        }

        var distance = Math.Abs(sdfNear) < Math.Abs(sdfFar) ? near : far;
        var point = o + (d * distance);
        var gradient = new Godot.Vector3(
            Sample(tool, point + Godot.Vector3.Right) - Sample(tool, point + Godot.Vector3.Left),
            Sample(tool, point + Godot.Vector3.Up) - Sample(tool, point + Godot.Vector3.Down),
            Sample(tool, point + Godot.Vector3.Back) - Sample(tool, point + Godot.Vector3.Forward));
        var normal = gradient.LengthSquared() > 1e-12 ? TerrainVectors.ToCore(gradient.Normalized()) : -Vector3.Normalize(direction);
        hit = new TerrainHit(TerrainVectors.ToCore(point), normal, (float)distance);
        return true;
    }

    internal static double Sample(VoxelTool tool, Godot.Vector3 p)
    {
        var x0 = Math.Floor(p.X);
        var y0 = Math.Floor(p.Y);
        var z0 = Math.Floor(p.Z);
        var fx = p.X - x0;
        var fy = p.Y - y0;
        var fz = p.Z - z0;
        var x = (int)x0;
        var y = (int)y0;
        var z = (int)z0;
        var c00 = Lerp(tool.GetVoxelF(new Vector3I(x, y, z)), tool.GetVoxelF(new Vector3I(x + 1, y, z)), fx);
        var c10 = Lerp(tool.GetVoxelF(new Vector3I(x, y + 1, z)), tool.GetVoxelF(new Vector3I(x + 1, y + 1, z)), fx);
        var c01 = Lerp(tool.GetVoxelF(new Vector3I(x, y, z + 1)), tool.GetVoxelF(new Vector3I(x + 1, y, z + 1)), fx);
        var c11 = Lerp(tool.GetVoxelF(new Vector3I(x, y + 1, z + 1)), tool.GetVoxelF(new Vector3I(x + 1, y + 1, z + 1)), fx);
        return Lerp(Lerp(c00, c10, fy), Lerp(c01, c11, fy), fz);
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
