using Godot;

namespace Horyzont.Game.UI;

/// <summary>The pause screen: the simulation of a host keeps running behind it.</summary>
public sealed partial class PauseMenu : Control
{
    /// <summary>Raised by resume.</summary>
    public event Action? ResumePressed;

    /// <summary>Raised by save.</summary>
    public event Action? SavePressed;

    /// <summary>Raised by invite.</summary>
    public event Action? InvitePressed;

    /// <summary>Raised by return to menu.</summary>
    public event Action? ReturnPressed;

    /// <summary>Raised by quit.</summary>
    public event Action? QuitPressed;

    /// <inheritdoc />
    public override void _Ready()
    {
        UiNodes.Bind(this, "%ResumeButton", () => ResumePressed?.Invoke());
        UiNodes.Bind(this, "%SaveButton", () => SavePressed?.Invoke());
        UiNodes.Bind(this, "%InviteButton", () => InvitePressed?.Invoke());
        UiNodes.Bind(this, "%ReturnButton", () => ReturnPressed?.Invoke());
        UiNodes.Bind(this, "%QuitButton", () => QuitPressed?.Invoke());
    }

    /// <summary>Shows only the buttons that apply.</summary>
    /// <param name="canSave">The player owns the world.</param>
    /// <param name="canInvite">A Steam lobby is active.</param>
    public void Prepare(bool canSave, bool canInvite)
    {
        GetNode<Control>("%SaveButton").Visible = canSave;
        GetNode<Control>("%InviteButton").Visible = canInvite;
    }
}
