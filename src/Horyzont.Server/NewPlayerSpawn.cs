using System.Numerics;

using Horyzont.Core.World;

namespace Horyzont.Server;

/// <summary>Chooses where a player who joins for the first time stands: the feet position in the planet frame and the body rotation.</summary>
/// <param name="planet">The planet the player starts on.</param>
/// <returns>The pose; the rotation must be normalized.</returns>
public delegate (Vector3 Feet, Quaternion BodyRotation) NewPlayerSpawn(PlanetParams planet);
