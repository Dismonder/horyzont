using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Simulation;

/// <summary>Values found by validation and used by resolution.</summary>
/// <param name="Planet">Planet of the intent.</param>
/// <param name="Point">Target point in the planet frame.</param>
/// <param name="Rotation">Rotation of a new construct.</param>
/// <param name="Cell">Target cell.</param>
/// <param name="Amount">An amount found by validation (K5-24: the tank capacity of a refuel).</param>
/// <param name="Velocity">Initial velocity of a reparent (K5-25: the ejection velocity).</param>
/// <param name="Entity">Entity a reparent moves (K5-25: the seated or evicted player).</param>
/// <param name="Frame">Target frame of a reparent (K5-25).</param>
/// <param name="ParentSeq">Current reparent counter of <paramref name="Entity"/> (K5-25).</param>
internal readonly record struct IntentFacts(
    PlanetId Planet, Vector3 Point, Quaternion Rotation, CellPos Cell, float Amount = 0f, Vector3 Velocity = default,
    EntityId Entity = default, FrameRef Frame = default, uint ParentSeq = 0);
