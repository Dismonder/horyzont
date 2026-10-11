using Horyzont.Core.World;

namespace Horyzont.Core.Space;

/// <summary>The result of <see cref="StarSystemGenesis"/>: the system and the planets of all its bodies.</summary>
/// <param name="System">The star system.</param>
/// <param name="Bodies">Planets in body index order; element 0 is the home planet.</param>
public sealed record GeneratedStarSystem(StarSystemParams System, IReadOnlyList<PlanetParams> Bodies);
