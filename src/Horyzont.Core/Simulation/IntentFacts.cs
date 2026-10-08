using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Simulation;

/// <summary>Values found by validation and used by resolution.</summary>
internal readonly record struct IntentFacts(PlanetId Planet, Vector3 Point, Quaternion Rotation, CellPos Cell);
