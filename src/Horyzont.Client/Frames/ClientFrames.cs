using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Frames;

/// <summary>
/// The client frames of a session (K5-20): built at the first server hello (home planet = the first announced planet; planet poses
/// from the given placement, K5-21 <c>ClientStarSystem</c>; its system the root), fed with full snapshots after the entity mirror and cleared on disconnect.
/// As an <see cref="IFrameHistoryView"/> it is the strict view rings rebase with. Main thread only.
/// </summary>
/// <param name="entities">The entity mirror.</param>
/// <param name="constructs">The construct mirror; null when the world is shared with the server.</param>
/// <param name="planets">Planet placement (K5-21: <c>ClientStarSystem</c>); null = the home planet of the hello in identity.</param>
public sealed class ClientFrames(EntityMirror entities, ConstructMirror? constructs, IPlanetPlacement? planets = null) : IClientSessionListener, IFullSnapshotSink, IFrameHistoryView
{
    /// <summary>Gets the frame history; null before the first hello.</summary>
    public ClientFrameHistory? History { get; private set; }

    /// <summary>Gets the frame graph; null before the first hello.</summary>
    public ClientFrameGraph? Graph { get; private set; }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        if (Graph is not null || hello.Planets.Count == 0)
        {
            History?.Clear();
            return;
        }

        var home = hello.Planets[0];
        var placement = planets ?? new HomeOnlyPlacement(home.Id);
        History = new ClientFrameHistory(entities, placement);
        Graph = new ClientFrameGraph(entities, constructs, History, placement, home.System);
    }

    /// <inheritdoc />
    public void OnFullSnapshot(Snapshot full) => History?.OnFullSnapshot(full);

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => History?.Clear();

    /// <inheritdoc />
    public bool TryGetWorldPose(FrameRef frame, double tick, out PoseD pose)
    {
        if (Graph is IFrameHistoryView view)
        {
            return view.TryGetWorldPose(frame, tick, out pose);
        }

        pose = PoseD.Identity;
        return false;
    }
}
