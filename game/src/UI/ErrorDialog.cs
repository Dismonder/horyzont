using System.Globalization;
using System.Text;

using Godot;

using Horyzont.Client.UI;

namespace Horyzont.Game.UI;

/// <summary>An error with its details and the recovery choices of a failed start, or a yes/no question.</summary>
public sealed partial class ErrorDialog : Control
{
    private readonly StringBuilder _text = new();

    /// <summary>Raised by the back button.</summary>
    public event Action? BackPressed;

    /// <summary>Raised by the restore button.</summary>
    public event Action? RestoreRequested;

    /// <summary>Raised by the recover button.</summary>
    public event Action? RecoverRequested;

    /// <summary>Raised by yes (true) and no (false) of a question.</summary>
    public event Action<bool>? Answered;

    /// <inheritdoc />
    public override void _Ready()
    {
        UiNodes.Bind(this, "%BackButton", () => BackPressed?.Invoke());
        UiNodes.Bind(this, "%RestoreLatestButton", () => RestoreRequested?.Invoke());
        UiNodes.Bind(this, "%RecoverCheckpointButton", () => RecoverRequested?.Invoke());
        UiNodes.Bind(this, "%YesButton", () => Answered?.Invoke(true));
        UiNodes.Bind(this, "%NoButton", () => Answered?.Invoke(false));
    }

    /// <summary>Shows the current error of the menu.</summary>
    /// <param name="menu">The menu state machine.</param>
    public void ShowError(MenuController menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        var outcome = menu.Outcome;
        GetNode<Label>("%TitleLabel").Text = "ui.error_title";
        GetNode<Label>("%MessageLabel").Text = menu.ErrorKey ?? "ui.start_failed";
        _text.Clear();
        switch (outcome?.Kind)
        {
            case UiStartKind.FromNewerVersion:
                _text.Append(string.Format(CultureInfo.InvariantCulture, Tr("ui.version_detail"), outcome.SchemaVersion, outcome.SupportedVersion));
                break;
            case UiStartKind.ReplayFailed:
                if (outcome.OpId is { } op)
                {
                    _text.Append(string.Format(CultureInfo.InvariantCulture, Tr("ui.replay_op"), op)).Append('\n');
                }

                _text.Append(outcome.Message);
                break;
            case UiStartKind.Corrupted:
                _text.Append(outcome.Message).Append('\n').Append(outcome.BackupCount > 0
                    ? string.Format(CultureInfo.InvariantCulture, Tr("ui.backups_found"), outcome.BackupCount)
                    : Tr("ui.no_backups"));
                break;
            default:
                _text.Append(menu.ErrorDetail);
                break;
        }

        GetNode<Label>("%DetailLabel").Text = _text.ToString();
        var asking = menu.Confirmation != UiConfirmation.None;
        GetNode<Control>("%ErrorSection").Visible = !asking;
        GetNode<Control>("%QuestionSection").Visible = asking;
        GetNode<Control>("%RestoreLatestButton").Visible = menu.CanRestore;
        GetNode<Control>("%RecoverCheckpointButton").Visible = menu.CanRecover;
        if (asking)
        {
            GetNode<Label>("%QuestionLabel").Text = menu.Confirmation switch
            {
                UiConfirmation.Restore => "ui.restore_confirm",
                UiConfirmation.Recover => "ui.recover_confirm",
                _ => "ui.invite_confirm",
            };
        }
    }

    /// <summary>Shows a question that is not an error (a friend's invite during a game).</summary>
    /// <param name="menu">The menu state machine.</param>
    public void ShowQuestion(MenuController menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        GetNode<Label>("%TitleLabel").Text = "ui.invite";
        GetNode<Control>("%ErrorSection").Visible = false;
        GetNode<Control>("%QuestionSection").Visible = true;
        GetNode<Label>("%QuestionLabel").Text = "ui.invite_confirm";
    }
}
