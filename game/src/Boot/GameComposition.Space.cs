using Horyzont.Client.Space;
using Horyzont.Server;
using Horyzont.Server.Frames;
using Horyzont.Server.Space;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Boot;

/// <summary>Star system wiring (K5-21): the server sends the system to ready players, the client places the planets with it.</summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the sender of <c>StarSystemState</c>; null without a server.</summary>
    public ServerSpaceSync? SpaceSync { get; private set; }

    /// <summary>Gets the zone transitions of ships; null without a server.</summary>
    public ServerZoneTransitions? ZoneTransitions { get; private set; }

    /// <summary>Gets the star system of the client session (planet placement of the client frames); null without a client.</summary>
    public ClientStarSystem? ClientStarSystem { get; private set; }

    // Runs before ConfigureReplication: the client frames are built with this placement.
    partial void ConfigureSpace()
    {
        if (Server is not null)
        {
            SpaceSync = ServerSpaceSync.Register(Server);

            // Phase Frames: the flight of ships (K5-24) before the zone transitions (README Y7, X10).
            var server = Server;
            ConfigureFlight(server);
            var frames = ServerFrames;
            ZoneTransitions = new ServerZoneTransitions(
                () => frames is null ? null : TransitionsOf(frames, server), GameServerHooks.TickRate, _env.Loggers.CreateLogger<ServerZoneTransitions>());

            // Last in phase Frames: the frame nodes follow the zone transitions before the characters step (K5-20 review).
            ServerZoneTransitions.RegisterFramesPhase(
                server, ZoneTransitions, ServerFrameNodes is { } nodes ? new FrameNodesComposer(nodes, ServerTickPhase.Frames) : null);
        }

        if (Client is not null)
        {
            ClientStarSystem = new ClientStarSystem();
            Client.AddListener(ClientStarSystem);
            DayNight?.StarSystem = ClientStarSystem;
        }
    }

    private static FrameTransitions? TransitionsOf(ServerFramesBinder frames, ServerRuntime server)
    {
        frames.EnsureWorld(server);
        return frames.Transitions;
    }
}
