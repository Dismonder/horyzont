using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;

using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Frames;

/// <summary>
/// The space of a planet frame (K5-20, decisions 17–18): global Godot values go through the planet node before
/// <see cref="PlanetFrames"/>, whose input is the local space of the planet node. Without a node (no <see cref="FrameNodes"/> yet,
/// tests) the planet stands in identity, which is where the home planet stands in EA. A value type: resolving it allocates nothing.
/// </summary>
/// <param name="planet">The planet.</param>
/// <param name="node">The node of the planet frame; null = identity.</param>
/// <param name="parameters">Parameters of the planet (its gravity); null = no gravity.</param>
public readonly struct PlanetSpace(PlanetId planet, Node3D? node = null, PlanetParams? parameters = null) : IFrameSpace, IEquatable<PlanetSpace>
{
    private readonly Node3D? _node = node;

    /// <summary>Gets the planet.</summary>
    public PlanetId Planet { get; } = planet;

    /// <summary>Gets the parameters of the planet; null when not given.</summary>
    public PlanetParams? Parameters { get; } = parameters;

    /// <inheritdoc />
    public FrameRef Frame => FrameRef.Of(Planet);

    /// <inheritdoc />
    public Rid PhysicsSpace => _node is { } node && GodotObject.IsInstanceValid(node) && node.IsInsideTree() ? node.GetWorld3D().Space : default;

    /// <inheritdoc />
    public Transform3D GlobalTransform => _node is { } node && GodotObject.IsInstanceValid(node) && node.IsInsideTree() ? node.GlobalTransform : Transform3D.Identity;

    /// <inheritdoc />
    public bool SupportsWalking => true;

    /// <summary>Compares two spaces.</summary>
    /// <param name="left">Left.</param>
    /// <param name="right">Right.</param>
    /// <returns>Whether they are equal.</returns>
    public static bool operator ==(PlanetSpace left, PlanetSpace right) => left.Equals(right);

    /// <summary>Compares two spaces.</summary>
    /// <param name="left">Left.</param>
    /// <param name="right">Right.</param>
    /// <returns>Whether they differ.</returns>
    public static bool operator !=(PlanetSpace left, PlanetSpace right) => !left.Equals(right);

    /// <inheritdoc />
    public Vector3D GravityAt(Vector3D localPosition) => Parameters is { } planet ? FrameGravity.AtPlanet(planet, localPosition) : Vector3D.Zero;

    /// <summary>A global Godot position in the planet frame.</summary>
    /// <param name="global">Global position.</param>
    /// <returns>The planet-frame position.</returns>
    public NumVector3 ToPlanetLocal(Vector3 global) => PlanetFrames.ToPlanetLocal(Planet, GlobalTransform.AffineInverse() * global);

    /// <summary>A global Godot direction or velocity in the planet frame.</summary>
    /// <param name="global">Global vector.</param>
    /// <returns>The planet-frame vector.</returns>
    public NumVector3 ToPlanetLocalDirection(Vector3 global) =>
        PlanetFrames.ToPlanetLocalDirection(Planet, GlobalTransform.Basis.Inverse() * global);

    /// <summary>A global Godot basis as a rotation of the planet frame.</summary>
    /// <param name="global">Global basis.</param>
    /// <returns>The normalized rotation.</returns>
    public NumQuaternion ToNumerics(Basis global) => PlanetFrames.ToNumerics(Planet, GlobalTransform.Basis.Inverse() * global);

    /// <summary>A planet-frame position as a global Godot position.</summary>
    /// <param name="local">Planet-frame position.</param>
    /// <returns>The global position.</returns>
    public Vector3 ToGodot(NumVector3 local) => GlobalTransform * PlanetFrames.ToGodot(Planet, local);

    /// <summary>A planet-frame direction or velocity as a global Godot vector.</summary>
    /// <param name="local">Planet-frame vector.</param>
    /// <returns>The global vector.</returns>
    public Vector3 ToGodotDirection(NumVector3 local) => GlobalTransform.Basis * PlanetFrames.ToGodotDirection(Planet, local);

    /// <summary>A planet-frame rotation as a global Godot basis.</summary>
    /// <param name="local">Planet-frame rotation.</param>
    /// <returns>The global basis.</returns>
    public Basis ToGodotBasis(NumQuaternion local) => GlobalTransform.Basis * PlanetFrames.ToGodotBasis(Planet, local);

    /// <inheritdoc />
    public bool Equals(PlanetSpace other) => Planet == other.Planet && ReferenceEquals(_node, other._node) && ReferenceEquals(Parameters, other.Parameters);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PlanetSpace other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Planet, _node, Parameters);
}
