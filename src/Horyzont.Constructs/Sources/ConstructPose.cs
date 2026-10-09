using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Sources;

/// <summary>Pose and extent of a construct in its parent frame.</summary>
/// <param name="Id">Construct entity.</param>
/// <param name="Parent">Parent frame.</param>
/// <param name="Origin">Corner of cell (0,0,0) in the parent frame.</param>
/// <param name="Rotation">Rotation from construct space to the parent frame.</param>
/// <param name="Version">Block data version.</param>
/// <param name="BoundsMin">Lowest occupied cell (conservative).</param>
/// <param name="BoundsMax">Highest occupied cell (conservative).</param>
public readonly record struct ConstructPose(
    EntityId Id, FrameRef Parent, Vector3 Origin, Quaternion Rotation, uint Version, CellPos BoundsMin, CellPos BoundsMax);
