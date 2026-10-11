using Godot;

using Horyzont.Client.Markers;
using Horyzont.Client.Navigation;
using Horyzont.Client.Tools;
using Horyzont.Client.UI;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Client.Navigation;
using Horyzont.Game.UI.Map;
using Horyzont.Server.Markers;

namespace Horyzont.Game.Boot;

/// <summary>Map markers, navigation and the maps (K5-27): the server sends markers to ready players, the client keeps them, steers to a target and draws the maps.</summary>
public sealed partial class GameComposition
{
    private IGameplayInputGate? _menuGate;

    /// <summary>Gets the sender of markers and marker operations; null without a server.</summary>
    public MarkerReplication? MarkerReplication { get; private set; }

    /// <summary>Gets the markers the client may see; null without a client.</summary>
    public ClientMarkerStore? ClientMarkers { get; private set; }

    /// <summary>Gets the navigation target and guidance (the flight HUD of K5-24 and K5-25 calls <c>TryGetGuidance</c>); null without a rendering client.</summary>
    public NavigationCoordinator? Navigation { get; private set; }

    /// <summary>Gets the map screens; null without a rendering client.</summary>
    public MapController? Map { get; private set; }

    /// <summary>Gets the gate that blocks gameplay input while the map is open; null without a rendering client.</summary>
    public MapInputGate? MapGate { get; private set; }

    /// <summary>
    /// Combines the gate of the menu (K3-18) with the gate of the map, so a closed menu does not let movement and tools through while
    /// the map is open. The menu keeps its own gate; <see cref="Map"/> asks it before it opens.
    /// </summary>
    /// <param name="menu">The gate of the menu.</param>
    /// <returns>The gate to give the input readers.</returns>
    public IGameplayInputGate ComposeInputGate(IGameplayInputGate menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        _menuGate = menu;
        return MapGate is { } map ? new CompositeInputGate(menu, map) : menu;
    }

    partial void ConfigureNavigation()
    {
        if (Server is not null)
        {
            // After the star system sync, so a ready player gets StarSystemState before MarkersState.
            MarkerReplication = MarkerReplication.Register(Server, () => ReplicationBudget);
        }

        if (Client is null)
        {
            return;
        }

        ClientMarkers = new ClientMarkerStore();
        Client.AddListener(ClientMarkers);
        if (!_env.Render || ClientNode is null || LocalPlayer is null || ClientFrames is null || ClientStarSystem is null)
        {
            return;
        }

        var entities = ClientReplication?.Entities;
        var viewpoint = new CameraViewpoint(ClientNode);
        var poses = new LazyFramePoses(ClientFrames);
        Func<double> renderTick = () => RemoteMotion?.Clock.RenderTick ?? 0;
        Navigation = new NavigationCoordinator(poses, ClientMarkers, entities, viewpoint);
        Client.AddListener(new ClearNavigationOnDisconnect(Navigation));
        var separator = TranslationServer.GetLocale().StartsWith("pl", StringComparison.Ordinal) ? ',' : '.';
        ClientNode.AddChild(new NavigationHud(Navigation, ClientMarkers, renderTick, separator));

        MapGate = new MapInputGate();
        var map = new MapController(MapGate);
        map.Initialize(
            ClientStarSystem, poses, ClientMarkers, entities, viewpoint, Navigation, new SessionIntentSender(Client.Session), renderTick,
            () => _menuGate?.IsBlocked != true, () => ClientNode.GetNodeOrNull<LocalInputReader>("Input")?.Recapture());
        Client.AddListener(map);
        Client.AddListener(map.Dialog);
        ClientNode.AddChild(map);
        Map = map;
    }

    /// <summary>A navigation target is client state only (P5): it ends with the session.</summary>
    private sealed class ClearNavigationOnDisconnect(NavigationCoordinator navigation) : Horyzont.Client.Session.IClientSessionListener
    {
        public void OnDisconnected(Horyzont.Core.Protocol.DisconnectCode code) => navigation.Clear();
    }
}
