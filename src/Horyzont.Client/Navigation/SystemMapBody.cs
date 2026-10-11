using Horyzont.Core.Ids;

namespace Horyzont.Client.Navigation;

/// <summary>A body on the system map: centre in the system frame and the zone of influence.</summary>
/// <param name="Planet">The planet or moon.</param>
/// <param name="BodyIndex">Index of the body in the system; 0 is the home planet.</param>
/// <param name="NameKey">Localization key of the name (<c>BODY_HOME</c>, <c>BODY_MOON</c>, <c>BODY_B</c>, <c>BODY_C</c>).</param>
/// <param name="X">System frame X of the centre, metres.</param>
/// <param name="Z">System frame Z of the centre, metres.</param>
/// <param name="RadiusMeters">Surface radius.</param>
/// <param name="ZoneRadiusMeters">Radius of the zone of influence (3 R).</param>
public readonly record struct SystemMapBody(PlanetId Planet, byte BodyIndex, string NameKey, double X, double Z, double RadiusMeters, double ZoneRadiusMeters);
