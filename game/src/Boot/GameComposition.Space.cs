using Horyzont.Client.Space;
using Horyzont.Server.Space;

namespace Horyzont.Game.Boot;

/// <summary>Star system wiring (K5-21): the server sends the system to ready players, the client places the planets with it.</summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the sender of <c>StarSystemState</c>; null without a server.</summary>
    public ServerSpaceSync? SpaceSync { get; private set; }

    /// <summary>Gets the star system of the client session (planet placement of the client frames); null without a client.</summary>
    public ClientStarSystem? ClientStarSystem { get; private set; }

    // Runs before ConfigureReplication: the client frames are built with this placement.
    partial void ConfigureSpace()
    {
        if (Server is not null)
        {
            SpaceSync = ServerSpaceSync.Register(Server);
        }

        if (Client is not null)
        {
            ClientStarSystem = new ClientStarSystem();
            Client.AddListener(ClientStarSystem);
        }
    }
}
