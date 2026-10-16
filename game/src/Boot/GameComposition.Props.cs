using Horyzont.Client.Props;
using Horyzont.Server.Props;

namespace Horyzont.Game.Boot;

// K6-31: the server side of harvestable props. Regrowth and the replication of harvested props are tick participants of every server (solo,
// host, dedicated); both stay idle without props content. They are registered after the entity replication, so the prop states of an
// operation follow its construct changes. The terrain service they sample the generated surface from is the one of the server terrain.
public sealed partial class GameComposition
{
    /// <summary>Gets the regrowth of harvested flora; null without a server terrain.</summary>
    public PropRegrowth? PropRegrowth { get; private set; }

    /// <summary>Gets the replication of harvested props to remote players; null without a server terrain.</summary>
    public PropReplicator? PropReplication { get; private set; }

    /// <summary>
    /// Gets the harvested props the local player sees (K6-31 D18): the states of message 45 on a remote client, the world of the server itself on
    /// a host and in solo; null without a client character.
    /// </summary>
    public IPropStateView? PropStates { get; private set; }

    partial void ConfigureProps()
    {
        if (Client is not null && LocalPlayer is not null && PropStates is null && Server is { } local)
        {
            // The world is shared with the server and loaded after the client is composed, so the simulation is asked at every lookup.
            PropStates = new SharedPropStates(() => local.RuntimeState is Horyzont.Server.ServerRuntimeState.Running or Horyzont.Server.ServerRuntimeState.SafeMode ? local.Simulation : null);
        }

        if (Server is not { } server || TerrainFactory is not { } factory)
        {
            return;
        }

        PropRegrowth = PropRegrowth.Register(
            server, () => factory.Terrain?.Service ?? throw new InvalidOperationException("The server terrain is not running."), _env.Loggers);
        PropReplication = PropReplicator.Register(server, _env.Loggers);
    }
}
