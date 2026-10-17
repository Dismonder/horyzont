using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol;

/// <summary>
/// Decides which samples of message 44 a client may apply (K6-35 D21): the channel is unreliable and not ordered against the reliable one, so a sample of the body
/// before a death must not overwrite the state after the respawn. A sample is accepted when its reparent counter is not older than the newest one known and, with the same
/// counter, its tick is not older than the newest accepted.
/// </summary>
public sealed class VitalsEpochFilter
{
    /// <summary>Learns the counter of a body that is about to exist (the target counter of a death notice), so older samples are refused before the first one of the new body.</summary>
    /// <param name="parentSeq">The reparent counter.</param>
    public void NoteEpoch(uint parentSeq) => throw new NotImplementedException("K6-35 B3.");

    /// <summary>Checks a sample and, when it is accepted, remembers it.</summary>
    /// <param name="tick">The tick of the sample.</param>
    /// <param name="parentSeq">The reparent counter of the sample.</param>
    /// <returns>True when the sample may be applied.</returns>
    public bool Accept(Tick tick, uint parentSeq) => throw new NotImplementedException("K6-35 B3.");
}
