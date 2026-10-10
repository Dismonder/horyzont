namespace Horyzont.Server.UI;

/// <summary>Public availability information; never includes disk paths, stacks or secrets.</summary>
/// <param name="ReadOnly">Whether changes are suspended.</param>
/// <param name="Reason">0 running, 1 terrain mismatch, 2 writer fault.</param>
/// <param name="Detail">Short technical text.</param>
public readonly record struct ServerStatusInfo(bool ReadOnly, byte Reason, string Detail);
