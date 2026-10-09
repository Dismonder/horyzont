using Horyzont.Core.Intents;

namespace Horyzont.Client.Tools;

/// <summary>No prediction: the effect appears when the server applies the operation.</summary>
public sealed class NoEditPrediction : IEditPrediction
{
    /// <summary>Gets the shared instance.</summary>
    public static NoEditPrediction Instance { get; } = new();

    /// <inheritdoc />
    public void Begin(uint clientSeq, Intent intent, in ToolPreview preview)
    {
    }

    /// <inheritdoc />
    public void Commit(uint clientSeq)
    {
    }

    /// <inheritdoc />
    public void Revert(uint clientSeq)
    {
    }
}
