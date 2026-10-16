using Horyzont.Core.Ids;
using Horyzont.Core.Props;

namespace Horyzont.Client.Props;

/// <summary>The prop under the crosshair.</summary>
/// <param name="Planet">The planet.</param>
/// <param name="Prop">The prop.</param>
/// <param name="Distance">Distance from the eye along the ray to the body of the prop, in metres.</param>
public readonly record struct PropPick(PlanetId Planet, PropInstance Prop, float Distance);
