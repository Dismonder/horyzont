using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Replication;

/// <summary>Receives reconstructed full snapshots, independent of wire decoding.</summary>
public interface IFullSnapshotSink
{
    /// <summary>Receives the newest full snapshot.</summary>
    /// <param name="full">Full snapshot.</param>
    void OnFullSnapshot(Snapshot full);
}
