using Horyzont.Game.Client.Blocks;

namespace Horyzont.Game.Boot;

/// <summary>
/// Functional blocks (K6-30). The lights of lit lamps belong to the construct world that is shown: the host's own world (solo or host
/// session) or the copy a client builds from the server (<see cref="RemoteConstructs"/>); a headless server draws no lights.
/// </summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the lamp lights of the shown construct world; null when nothing is drawn (dedicated server, no client yet).</summary>
    public BlockLights? BlockLights => Constructs?.World.Lights ?? RemoteConstructs?.World?.Lights;
}
