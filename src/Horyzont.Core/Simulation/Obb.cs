using System.Numerics;

namespace Horyzont.Core.Simulation;

/// <summary>Oriented box: a centre, a rotation from box space to the working frame and the half extents along the box axes.</summary>
/// <param name="Center">Centre.</param>
/// <param name="Rotation">Rotation from box space to the working frame.</param>
/// <param name="HalfExtents">Half extents along the box axes.</param>
public readonly record struct Obb(Vector3 Center, Quaternion Rotation, Vector3 HalfExtents);
