using Horyzont.Core.Survival;

namespace Horyzont.Core.Content;

/// <summary>A hazard profile of a body (K6-34, D8): the rates by day and by night and the zones of heights.</summary>
/// <param name="Key">Profile key, e.g. <c>core:temperate</c>.</param>
/// <param name="Day">Resource points per second by day, each 0 to 10.</param>
/// <param name="Night">Resource points per second by night, each 0 to 10.</param>
/// <param name="Zones">Zones in ascending order of <see cref="HazardZoneDef.MinHeight"/>; may be empty.</param>
public sealed record HazardProfileDef(string Key, HazardRates Day, HazardRates Night, IReadOnlyList<HazardZoneDef> Zones);
