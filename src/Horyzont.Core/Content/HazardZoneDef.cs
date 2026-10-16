using Horyzont.Core.Survival;

namespace Horyzont.Core.Content;

/// <summary>A zone of heights of a hazard profile (K6-34, D9): from <see cref="MinHeight"/> up the rates are scaled.</summary>
/// <param name="MinHeight">Height above the planet radius in metres, -10000 to 100000.</param>
/// <param name="Scale">The factor of each hazard, 0 to 10; a hazard the file does not name has 1.</param>
public sealed record HazardZoneDef(float MinHeight, HazardRates Scale);
