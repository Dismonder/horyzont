using Horyzont.Core.Protocol;

namespace Horyzont.Client.UI;

/// <summary>Translation keys for start, refusal and disconnect errors; details remain plain text.</summary>
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

    /// <summary>Gets a key for a refusal of the handshake.</summary>
    public static string KeyFor(HandshakeRejectCode code) => code switch
    {
        HandshakeRejectCode.WrongProtocol => "ui.reject_wrong_protocol",
        HandshakeRejectCode.WrongBuild => "ui.reject_wrong_build",
        HandshakeRejectCode.ServerFull => "ui.reject_full",
        HandshakeRejectCode.BadPassword => "ui.reject_bad_password",
        HandshakeRejectCode.Banned => "ui.reject_banned",
        HandshakeRejectCode.AlreadyConnected => "ui.reject_already_connected",
        HandshakeRejectCode.IdentityMismatch => "ui.reject_identity",
        _ => "ui.reject_unknown",
    };

    /// <summary>Gets a key for a <c>JoinUnavailable</c> reason; an unknown reason gets the generic text.</summary>
    /// <param name="reason">1 terrain semantics mismatch, 2 writer fault.</param>
    public static string KeyForJoinUnavailable(byte reason) => reason switch
    {
        1 => "ui.join_unavailable_terrain",
        2 => "ui.join_unavailable_writer",
        _ => "ui.join_unavailable",
    };
}
