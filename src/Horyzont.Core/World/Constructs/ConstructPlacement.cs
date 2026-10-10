using System.Numerics;

using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.World.Constructs;

/// <summary>
/// Pose of a new construct placed on the terrain (docs/plans/K1-03, "PlaceBlockIntent na terenie"). The validator and the
/// client preview share this one implementation, so the ghost of a block is exactly the construct the server creates.
/// </summary>
public static class ConstructPlacement
{
    private const float MinForwardLength = 1e-3f;

    /// <summary>
    /// Local +Y points along <see cref="TerrainMath.UpAt"/> of the hit, local −Z along <paramref name="lookForward"/> projected on
    /// the tangent plane (fallbacks: <paramref name="rayDirection"/>, the planet X axis, then the planet Z axis); the origin is the
    /// hit minus the rotated (0.5, 0, 0.5), so the hit is the middle of the bottom face of cell (0,0,0).
    /// </summary>
    /// <param name="hit">Terrain hit in the planet frame.</param>
    /// <param name="lookForward">Look direction of the player.</param>
    /// <param name="rayDirection">Direction of the ray that hit the terrain.</param>
    /// <param name="origin">Receives the corner of cell (0,0,0) in the planet frame.</param>
    /// <param name="rotation">Receives the rotation from construct space to the planet frame.</param>
    public static void OnTerrain(Vector3 hit, Vector3 lookForward, Vector3 rayDirection, out Vector3 origin, out Quaternion rotation)
    {
        var up = TerrainMath.UpAt(hit);
        var forward = Geometry.ProjectOnPlane(lookForward, up);
        if (forward.Length() < MinForwardLength)
        {
            forward = Geometry.ProjectOnPlane(rayDirection, up);
        }

        if (forward.Length() < MinForwardLength)
        {
            forward = Geometry.ProjectOnPlane(Vector3.UnitX, up);
        }

        if (forward.Length() < MinForwardLength)
        {
            forward = Geometry.ProjectOnPlane(Vector3.UnitZ, up);
        }

        forward = Vector3.Normalize(forward);
        var zAxis = -forward;
        var xAxis = Vector3.Normalize(Vector3.Cross(up, zAxis));
        var matrix = new Matrix4x4(
            xAxis.X, xAxis.Y, xAxis.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            zAxis.X, zAxis.Y, zAxis.Z, 0f,
            0f, 0f, 0f, 1f);
        rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
        origin = hit - Vector3.Transform(new Vector3(0.5f, 0f, 0.5f), rotation);
    }
}
