using Horyzont.Core.Protocol;

namespace Horyzont.Client.UI;

/// <summary>Translation keys for start and disconnect errors; details remain plain text.</summary>
public static class UiErrorMapper
{
    /// <summary>Gets a key for a world start result.</summary>
    public static string KeyFor(UiStartKind kind) => kind switch
    {
        UiStartKind.Started => "ui.started",
        UiStartKind.StartedReadOnly => "ui.read_only",
        UiStartKind.Corrupted => "ui.corrupted",
        UiStartKind.FromNewerVersion => "ui.newer_version",
        UiStartKind.ReplayFailed => "ui.replay_failed",
        UiStartKind.Aborted => "ui.aborted",
        _ => "ui.start_failed",
    };

    /// <summary>Gets a key for a disconnect reason.</summary>
    public static string KeyFor(DisconnectCode code) => code switch
    {
        DisconnectCode.Normal => "ui.disconnected",
        DisconnectCode.Timeout or DisconnectCode.HandshakeTimeout => "ui.timeout",
        DisconnectCode.ProtocolViolation => "ui.protocol_violation",
        DisconnectCode.Kicked => "ui.kicked",
        DisconnectCode.ServerShutdown or DisconnectCode.HostLeft => "ui.host_left",
        _ => "ui.disconnected",
    };
}
