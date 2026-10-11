using Horyzont.Core.Ids;
using Horyzont.Core.World.Markers;

namespace Horyzont.Client.Navigation;

/// <summary>A marker on the system map, placed in the system frame.</summary>
/// <param name="Id">Marker id.</param>
/// <param name="Name">Name.</param>
/// <param name="Icon">Icon.</param>
/// <param name="Color">Colour.</param>
/// <param name="IsShared">True for a shared marker.</param>
/// <param name="Planet">Planet whose frame holds the marker.</param>
/// <param name="X">System frame X, metres.</param>
/// <param name="Y">System frame Y, metres.</param>
/// <param name="Z">System frame Z, metres.</param>
public readonly record struct SystemMapMarker(MarkerId Id, string Name, MarkerIcon Icon, MarkerColor Color, bool IsShared, PlanetId Planet, double X, double Y, double Z);
