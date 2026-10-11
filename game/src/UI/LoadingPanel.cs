using Godot;

namespace Horyzont.Game.UI;

/// <summary>Shows the stage of a start or a shutdown, with a cancel button while cancelling is possible.</summary>
public sealed partial class LoadingPanel : Control
{
    /// <summary>Raised by the cancel button.</summary>
    public event Action? CancelPressed;

    /// <inheritdoc />
    public override void _Ready() => UiNodes.Bind(this, "%CancelButton", () => CancelPressed?.Invoke());

    /// <summary>Shows a stage.</summary>
    /// <param name="key">Translation key of the stage text.</param>
    /// <param name="cancellable">Whether cancel is possible.</param>
    public void SetStage(string key, bool cancellable)
    {
        GetNode<Label>("%StageLabel").Text = key;
        GetNode<BaseButton>("%CancelButton").Disabled = !cancellable;
    }
}
