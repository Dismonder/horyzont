using Godot;

namespace Horyzont.Game.UI;

/// <summary>The first screen: solo, host, join, quit. Only raises events; the coordinator decides.</summary>
public sealed partial class MainMenu : Control
{
    /// <summary>Raised by the solo button.</summary>
    public event Action? SoloPressed;

    /// <summary>Raised by the host button.</summary>
    public event Action? HostPressed;

    /// <summary>Raised by the join button.</summary>
    public event Action? JoinPressed;

    /// <summary>Raised by the quit button.</summary>
    public event Action? QuitPressed;

    /// <inheritdoc />
    public override void _Ready()
    {
        UiNodes.Bind(this, "%SoloButton", () => SoloPressed?.Invoke());
        UiNodes.Bind(this, "%HostButton", () => HostPressed?.Invoke());
        UiNodes.Bind(this, "%JoinButton", () => JoinPressed?.Invoke());
        UiNodes.Bind(this, "%QuitButton", () => QuitPressed?.Invoke());
    }

    /// <summary>Enables or disables the start buttons (never the quit button).</summary>
    /// <param name="enabled">Whether starting is possible.</param>
    public void SetStartsEnabled(bool enabled)
    {
        foreach (var name in new[] { "%SoloButton", "%HostButton", "%JoinButton" })
        {
            GetNode<BaseButton>(name).Disabled = !enabled;
        }
    }
}
