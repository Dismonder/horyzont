using Godot;

using Horyzont.Client.Steam;

namespace Horyzont.Game.Boot;

/// <summary>Process-wide node that runs the Steam callbacks and the lobby controller once per frame; a child of <see cref="Boot"/>, never of a session.</summary>
public partial class SteamLifetime : Node
{
    private SteamPlatformServices? _services;

    /// <summary>Gets the number of frames pumped (for tests).</summary>
    public int Pumped { get; private set; }

    /// <summary>Gives the node the services to pump. Call once, before the node enters the tree.</summary>
    /// <param name="services">Process-wide Steam services.</param>
    public void Initialize(SteamPlatformServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public override void _Process(double delta)
    {
        if (_services is null)
        {
            return;
        }

        _services.Pump();
        Pumped++;
    }
}
