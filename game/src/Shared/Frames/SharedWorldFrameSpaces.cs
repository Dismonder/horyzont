using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Game.Frames;

/// <summary>
/// The frame spaces of EA (K5-20, decision 18): every frame shares the physics space of the world. A planet frame has the radial
/// gravity of the planet; a construct frame has the gravity of its region expressed in the construct frame and supports walking only
/// in the static mode; the system frame has no gravity (zones of influence come with K5-21).
/// </summary>
/// <param name="nodes">The frame nodes of the side (transforms and the physics space).</param>
/// <param name="graph">The frame graph of the side; null before the world runs.</param>
/// <param name="planets">Parameters of a planet; null when unknown.</param>
/// <param name="constructMode">Mode of a construct; null when unknown.</param>
public sealed class SharedWorldFrameSpaces(
    FrameNodes nodes, Func<FrameGraph?> graph, Func<PlanetId, PlanetParams?> planets, Func<EntityId, ConstructMode?> constructMode)
{
    private readonly FrameNodes _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    private readonly Func<FrameGraph?> _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    private readonly Func<PlanetId, PlanetParams?> _planets = planets ?? throw new ArgumentNullException(nameof(planets));
    private readonly Func<EntityId, ConstructMode?> _constructMode = constructMode ?? throw new ArgumentNullException(nameof(constructMode));

    /// <summary>The space of a planet frame.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The space.</returns>
    public PlanetSpace Planet(PlanetId planet) => _nodes.PlanetSpaceOf(planet, _planets(planet));

    /// <summary>The space of any frame.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="space">Receives the space.</param>
    /// <returns>False for an unknown frame.</returns>
    public bool TryGet(FrameRef frame, out IFrameSpace space)
    {
        switch (frame.Kind)
        {
            case FrameKind.Planet when _planets(new PlanetId(frame.Id)) is not null:
                space = Planet(new PlanetId(frame.Id));
                return true;
            case FrameKind.Construct when _constructMode(new EntityId(frame.Id)) is { } mode:
                space = new FrameSpace(this, frame, mode == ConstructMode.Static);
                return true;
            case FrameKind.System:
                space = new FrameSpace(this, frame, false);
                return true;
            default:
                space = null!;
                return false;
        }
    }

    private sealed class FrameSpace(SharedWorldFrameSpaces owner, FrameRef frame, bool walking) : IFrameSpace
    {
        public FrameRef Frame => frame;

        public Rid PhysicsSpace => GodotObject.IsInstanceValid(owner._nodes) && owner._nodes.IsInsideTree() ? owner._nodes.GetWorld3D().Space : default;

        public Transform3D GlobalTransform =>
            owner._nodes.TryGetNode(frame, out var node) && node.IsInsideTree() ? node.GlobalTransform : Transform3D.Identity;

        public bool SupportsWalking => walking;

        public Vector3D GravityAt(Vector3D localPosition) =>
            frame.Kind != FrameKind.System && owner._graph() is { } g && FrameGravity.TryAt(g, frame, localPosition, owner._planets, out var gravity)
                ? gravity
                : Vector3D.Zero;
    }
}
