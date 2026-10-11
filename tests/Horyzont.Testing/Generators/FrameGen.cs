using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Frames;

namespace Horyzont.Testing.Generators;

/// <summary>FsCheck generators for double-precision frame types (K5-20).</summary>
public static class FrameGen
{
    /// <summary>Positions with components in ±200 km at millimetre steps.</summary>
    public static Gen<Vector3D> Position { get; } =
        Gen.Choose(-200_000_000, 200_000_000).ArrayOf(3).Select(a => new Vector3D(a[0] / 1000.0, a[1] / 1000.0, a[2] / 1000.0));

    /// <summary>Unit rotations (normalized random quaternions; near-zero draws fall back to identity).</summary>
    public static Gen<QuaternionD> Rotation { get; } =
        Gen.Choose(-1_000_000, 1_000_000).ArrayOf(4).Select(a =>
        {
            var q = new QuaternionD(a[0], a[1], a[2], a[3]);
            return q.LengthSquared() < 1e-6 ? QuaternionD.Identity : QuaternionD.Normalize(q);
        });

    /// <summary>Poses from <see cref="Position"/> and <see cref="Rotation"/>.</summary>
    public static Gen<PoseD> Pose { get; } = Position.SelectMany(p => Rotation.Select(r => new PoseD(p, r)));

    /// <summary>Arbitraries for <c>[Property(Arbitrary = [typeof(FrameGen.Arbitraries)])]</c>.</summary>
    public static class Arbitraries
    {
        /// <summary>Positions.</summary>
        /// <returns>The arbitrary.</returns>
        public static Arbitrary<Vector3D> Vectors() => Arb.From(Position);

        /// <summary>Rotations.</summary>
        /// <returns>The arbitrary.</returns>
        public static Arbitrary<QuaternionD> Rotations() => Arb.From(Rotation);

        /// <summary>Poses.</summary>
        /// <returns>The arbitrary.</returns>
        public static Arbitrary<PoseD> Poses() => Arb.From(Pose);
    }
}
