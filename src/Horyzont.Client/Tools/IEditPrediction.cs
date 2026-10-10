using Horyzont.Core.Intents;

namespace Horyzont.Client.Tools;

/// <summary>
/// Local, provisional effect of an intent that waits for the server. K2 uses <see cref="NoEditPrediction"/> for every mode (solo and
/// host share the authoritative terrain, a remote client shows no prediction before K3-15 and K3-16 bring their own implementations).
/// </summary>
public interface IEditPrediction
{
    /// <summary>An intent was sent.</summary>
    /// <param name="clientSeq">Its client sequence number.</param>
    /// <param name="intent">The intent.</param>
    /// <param name="preview">The preview at the time of sending.</param>
    void Begin(uint clientSeq, Intent intent, in ToolPreview preview);

    /// <summary>The server accepted the intent; the authoritative data replaces the prediction.</summary>
    /// <param name="clientSeq">Client sequence number.</param>
    void Commit(uint clientSeq);

    /// <summary>The intent failed (rejected, timed out or disconnected); called from the newest to the oldest.</summary>
    /// <param name="clientSeq">Client sequence number.</param>
    void Revert(uint clientSeq);
}
