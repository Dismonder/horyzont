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
internal readonly record struct IntentFacts(PlanetId Planet, Vector3 Point, Quaternion Rotation, CellPos Cell, float Amount = 0f);
