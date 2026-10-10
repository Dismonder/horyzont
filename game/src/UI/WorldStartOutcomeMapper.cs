using Horyzont.Client.UI;
using Horyzont.Server;

namespace Horyzont.Game.UI;

/// <summary>Turns the server's <see cref="WorldStartResult"/> into the plain model the menu shows; the Client project never references the Server project.</summary>
public static class WorldStartOutcomeMapper
{
    /// <summary>Maps one start result.</summary>
    /// <param name="result">The result of <c>ServerRuntime.StartAsync</c>.</param>
    /// <returns>The model for the menu.</returns>
    public static UiStartOutcome Map(WorldStartResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            WorldStartResult.Started => new UiStartOutcome(UiStartKind.Started, string.Empty, 0, null, null, null, false),
            WorldStartResult.StartedReadOnly readOnly => new UiStartOutcome(
                UiStartKind.StartedReadOnly, readOnly.Message, 0, null, null, null, readOnly.Reason == ReadOnlyReason.TerrainSemanticsMismatch),
            WorldStartResult.Corrupted corrupted => new UiStartOutcome(
                UiStartKind.Corrupted, corrupted.Message, corrupted.Backups.Count, null, null, null, false),
            WorldStartResult.FromNewerVersion newer => new UiStartOutcome(
                UiStartKind.FromNewerVersion, string.Empty, 0, null, newer.SchemaVersion, newer.SupportedVersion, false),
            WorldStartResult.ReplayFailed failed => new UiStartOutcome(
                UiStartKind.ReplayFailed, failed.Message, 0, failed.OpId.Value == 0 ? null : failed.OpId.Value, null, null, false),
            WorldStartResult.Aborted => new UiStartOutcome(UiStartKind.Aborted, string.Empty, 0, null, null, null, false),
            WorldStartResult.StartFailed failed => new UiStartOutcome(UiStartKind.StartFailed, failed.Message, 0, null, null, null, false),
            _ => new UiStartOutcome(UiStartKind.StartFailed, result.GetType().Name, 0, null, null, null, false),
        };
    }
}
