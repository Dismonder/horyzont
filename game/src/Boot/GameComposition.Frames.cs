using Horyzont.Client.Frames;
using Horyzont.Client.Replication;
using Horyzont.Server;
using Horyzont.Server.Frames;

namespace Horyzont.Game.Boot;

/// <summary>Reference frames (K5-20): server frame history and transitions, client frames.</summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the server frames (history, transitions); null without a server.</summary>
    public ServerFramesBinder? ServerFrames { get; private set; }

    /// <summary>Gets the client frames of the session; null without a replicating client.</summary>
    public ClientFrames? ClientFrames { get; private set; }

    /// <summary>
    /// Registers the server frames before any other participant of the compose phase (the history is the first one there, Y7);
    /// the console reaches the transitions through the returned binder once the world runs.
    /// </summary>
    /// <param name="server">The server runtime.</param>
    /// <returns>The binder.</returns>
    private static ServerFramesBinder RegisterServerFrames(ServerRuntime server)
    {
        var frames = new ServerFramesBinder();
        frames.Register(server);
        return frames;
    }

    private void ConfigureClientFrames(ClientReplication replication, ConstructMirror? mirror)
    {
        var frames = new ClientFrames(replication.Entities, mirror);
        replication.Decoder.AddSink(frames);
        Client!.AddListener(frames);
        LocalPlayer!.AttachFrames(frames);
        ClientFrames = frames;
    }
}
