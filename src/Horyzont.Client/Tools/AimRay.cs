using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Client.Tools;

/// <summary>Aim in the planet frame of the local player. The origin is always the eye, in first and third person.</summary>
/// <param name="Planet">Planet of the frame.</param>
/// <param name="Eye">Ray origin: feet + UpAt(feet) * EyeHeight, the server formula.</param>
/// <param name="Direction">Unit direction of the ray.</param>
/// <param name="LookForward">Unit direction the body looks to; orients a new construct.</param>
/// <param name="Feet">Feet of the player (for the capsule hints of the preview).</param>
public readonly record struct AimRay(PlanetId Planet, Vector3 Eye, Vector3 Direction, Vector3 LookForward, Vector3 Feet);
