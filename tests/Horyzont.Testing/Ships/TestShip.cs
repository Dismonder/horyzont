using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Testing.Ships;

/// <summary>A construct for frame tests (K5-20): a floor of <paramref name="Size"/> x 1 x <paramref name="Size"/> stone cells.</summary>
/// <param name="Id">Entity id.</param>
/// <param name="Parent">Parent frame.</param>
/// <param name="Origin">Corner of cell (0,0,0) in the parent frame.</param>
/// <param name="Rotation">Rotation to the parent frame.</param>
/// <param name="Mode">Construct mode; <see cref="ConstructMode.Ship"/> by default.</param>
/// <param name="Size">Edge of the floor in cells.</param>
public sealed record TestShip(EntityId Id, FrameRef Parent, Vector3 Origin, Quaternion Rotation, ConstructMode Mode = ConstructMode.Ship, int Size = 4);
