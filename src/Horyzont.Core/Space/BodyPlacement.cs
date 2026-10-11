using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Core.Space;

/// <summary>Where one body of the star system stands (K5-21 decision 2). Saved type (blob 10, message 32).</summary>
/// <param name="Planet">The planet of the body; its radius and gravity live in <see cref="World.PlanetParams"/>.</param>
/// <param name="BodyIndex">Index of the body in the system; 0 is the home planet.</param>
/// <param name="Pose">Pose of the planet frame in the system frame; the rotation is the identity in early access.</param>
public readonly record struct BodyPlacement(PlanetId Planet, byte BodyIndex, PoseD Pose);
