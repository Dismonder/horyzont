using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Frame in a snapshot.</summary>
/// <param name="Frame">The frame.</param>
/// <param name="ParentSeq">Reparent counter of the frame itself (a construct); 0 for planets and systems.</param>
public readonly record struct SnapshotFrame(FrameRef Frame, uint ParentSeq);
