using Godot;

using Horyzont.Client.Frames;
using Horyzont.Client.Replication;
using Horyzont.Game.Frames;
using Horyzont.Server;
using Horyzont.Server.Frames;

namespace Horyzont.Game.Boot;

/// <summary>
/// Reference frames (K5-20): server frame history and transitions, client frames, and the frame nodes of each side
/// (<c>/Root/Server/Frames</c>, <c>/Root/Client/Frames</c>; decision 16). Server tick: frames → characters → compose (history first,
/// then the frame nodes). Client frame: poll (snapshots, frame history) → own character → frame nodes (<see cref="FrameNodes.ComposePriority"/>)
/// → remote presentations and render interpolation.
/// </summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the server frames (history, transitions); null without a server.</summary>
    public ServerFramesBinder? ServerFrames { get; private set; }

    /// <summary>Gets the frame nodes of the server (<c>/Root/Server/Frames</c>); null without a server.</summary>
    public FrameNodes? ServerFrameNodes { get; private set; }

    /// <summary>Gets the client frames of the session; null without a replicating client.</summary>
    public ClientFrames? ClientFrames { get; private set; }

    /// <summary>Gets the frame nodes of the client (<c>/Root/Client/Frames</c>); null without a replicating client.</summary>
    public FrameNodes? ClientFrameNodes { get; private set; }

    /// <summary>
    /// Registers the server frames before any other participant of the compose phase (the history is the first one there, Y7), then the
    /// frame nodes of the server, composed right after the history; the console reaches the transitions through the returned binder.
    /// </summary>
    /// <param name="server">The server runtime.</param>
    /// <param name="serverNode">The server node (<c>/Root/Server</c>).</param>
    /// <returns>The binder and the server frame nodes.</returns>
    private static (ServerFramesBinder Frames, FrameNodes Nodes) RegisterServerFrames(ServerRuntime server, Node serverNode)
    {
        var frames = new ServerFramesBinder();
        frames.Register(server);
        var nodes = new FrameNodes { Name = "Frames" };
        nodes.Initialize(() => frames.Graph?.Source);
        serverNode.AddChild(nodes);
        server.AddTickParticipant(new FrameNodesComposer(nodes));
        return (frames, nodes);
    }

    private void ConfigureClientFrames(ClientReplication replication, ConstructMirror? mirror)
    {
        var frames = new ClientFrames(replication.Entities, mirror, ClientStarSystem);
        replication.Decoder.AddSink(frames);
        Client!.AddListener(frames);
        LocalPlayer!.AttachFrames(frames);
        ClientFrames = frames;

        var nodes = new FrameNodes { Name = "Frames", AutoCompose = true };
        nodes.Initialize(() => frames.Graph);
        ClientNode!.AddChild(nodes);
        ClientFrameNodes = nodes;
        LocalPlayer.FrameNodes = nodes;
        _ownTerrain?.FrameNodes = nodes;
        DayNight?.FrameNodes = nodes;
        Tools?.FrameNodes = nodes;
    }

    /// <summary>
    /// Composes the server frame nodes: in the compose phase after the frame history, and (K5-21) once more at the end of the frames
    /// phase, so the characters of the tick see the frames moved and reparented in that phase, not those of the previous tick.
    /// </summary>
    private sealed class FrameNodesComposer(FrameNodes nodes, ServerTickPhase phase = ServerTickPhase.Compose) : IServerTickParticipant
    {
        public ServerTickPhase Phase => phase;

        public void OnTick(ServerRuntime runtime)
        {
            if (GodotObject.IsInstanceValid(nodes))
            {
                nodes.Compose();
            }
        }
    }
}
