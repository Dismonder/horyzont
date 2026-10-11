using Horyzont.Client.UI;

namespace Horyzont.Game.UI.Map;

/// <summary>Blocks movement and tools while the map is open (K5-27 decision 12).</summary>
public sealed class MapInputGate : IGameplayInputGate
{
    /// <summary>Gets or sets a value indicating whether the map is open.</summary>
    public bool IsOpen { get; set; }

    /// <inheritdoc />
    public bool IsBlocked => IsOpen;
}
