using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Tools;

/// <summary>What to draw. <see cref="Hint"/> is the locally predicted reject reason (None = green); it never replaces server validation.</summary>
/// <param name="Kind">What to draw.</param>
/// <param name="Center">Sphere centre or centre of the cell, planet frame.</param>
/// <param name="Radius">Sphere radius (sphere only).</param>
/// <param name="Origin">Origin of the construct pose of the ghost, planet frame.</param>
/// <param name="Rotation">Rotation of the construct pose of the ghost.</param>
/// <param name="Construct">Construct the block is added to (construct cell only).</param>
/// <param name="Cell">Cell of the new block.</param>
/// <param name="Orientation">Orientation of the block.</param>
/// <param name="Hint">Predicted reject reason.</param>
public readonly record struct ToolPreview(
    ToolPreviewKind Kind,
    Vector3 Center,
    float Radius,
    Vector3 Origin,
    Quaternion Rotation,
    EntityId Construct,
    CellPos Cell,
    byte Orientation,
    RejectReason Hint);
