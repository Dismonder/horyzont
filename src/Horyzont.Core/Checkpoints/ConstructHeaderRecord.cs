using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Checkpoints;

/// <summary>Header of a construct in a checkpoint and in <see cref="Protocol.Messages.ConstructState"/>.</summary>
/// <param name="Id">Construct entity.</param>
/// <param name="Parent">Parent frame.</param>
/// <param name="ParentSeq">Reparent counter.</param>
/// <param name="Mode">Mode.</param>
/// <param name="Origin">Corner of cell (0,0,0) in the parent frame.</param>
/// <param name="Rotation">Rotation from construct space to the parent frame.</param>
/// <param name="Version">Block data version.</param>
public sealed record ConstructHeaderRecord(
    EntityId Id, FrameRef Parent, uint ParentSeq, ConstructMode Mode, Vector3 Origin, Quaternion Rotation, uint Version)
{
    /// <summary>Header of a live construct.</summary>
    /// <param name="construct">The construct.</param>
    /// <returns>The record.</returns>
    public static ConstructHeaderRecord From(Construct construct)
    {
        ArgumentNullException.ThrowIfNull(construct);
        return new(construct.Id, construct.Parent, construct.ParentSeq, construct.Mode, construct.Origin, construct.Rotation, construct.Version);
    }
}
