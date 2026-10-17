namespace Horyzont.Core.Content;

/// <summary>The assignment of a hazard profile to a body of the star system (K6-34, D8, D9).</summary>
/// <param name="Body">Index of the body, 0 to 8.</param>
/// <param name="Profile">Key of the profile.</param>
public sealed record HazardBodyDef(byte Body, string Profile);
