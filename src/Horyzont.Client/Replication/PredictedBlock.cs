using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Replication;

/// <summary>One visual, not yet confirmed block edit of the local player.</summary>
/// <param name="ClientSeq">Sequence number of the intent that caused it.</param>
/// <param name="Construct">Existing construct, or <see cref="EntityId.None"/> for the ghost of a new construct.</param>
/// <param name="Cell">Edited cell (cell 0,0,0 of a new construct).</param>
/// <param name="Block">The block shown there; empty for a predicted removal.</param>
/// <param name="Origin">Planet-frame origin of the new construct ghost (unused for existing constructs).</param>
/// <param name="Rotation">Planet-frame rotation of the new construct ghost.</param>
/// <param name="Accepted">True once the server accepted the intent and only its operation is awaited.</param>
public readonly record struct PredictedBlock(
    uint ClientSeq, EntityId Construct, CellPos Cell, BlockState Block, Vector3 Origin, Quaternion Rotation, bool Accepted)
{
    /// <summary>Gets a value indicating whether this is the ghost of a construct that does not exist yet.</summary>
    public bool IsNewConstruct => Construct.IsNone;
}
