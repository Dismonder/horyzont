namespace Horyzont.Constructs.Content;

/// <summary>Visual parameters of the light of a lamp block (K6-30 D5); content, never part of Core.</summary>
/// <param name="RangeMeters">Range of the light, metres.</param>
/// <param name="Color">Colour as 0xRRGGBBAA.</param>
/// <param name="Energy">Light energy.</param>
public sealed record BlockLightDef(float RangeMeters, uint Color, float Energy);
