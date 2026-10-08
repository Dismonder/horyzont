namespace Horyzont.Persistence.Faults;

/// <summary>Places in the writer where tests inject failures.</summary>
public enum FaultPoint
{
    /// <summary>Journal rows are inserted, the transaction is not committed yet.</summary>
    BeforeJournalCommit,

    /// <summary>
    /// The journal batch is committed, the writer has not reported it yet. An error here is an ambiguous commit: the writer treats
    /// it as failed (entries stay pending, nothing is acknowledged) although the rows are on disk.
    /// </summary>
    AfterJournalCommit,

    /// <summary>Part of the checkpoint rows are written.</summary>
    CheckpointMidTransaction,

    /// <summary>Every checkpoint row and the journal deletion are written, the transaction is not committed yet.</summary>
    BeforeCheckpointCommit,

    /// <summary>The checkpoint is committed, the writer has not reported it yet.</summary>
    AfterCheckpointCommit,
}
