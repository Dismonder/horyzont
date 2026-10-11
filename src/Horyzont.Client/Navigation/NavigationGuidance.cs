using Horyzont.Core.Frames;

namespace Horyzont.Client.Navigation;

/// <summary>Direction and distance to the navigation target in the world (system frame), double precision.</summary>
/// <param name="WorldDelta">Target position minus own position in the system frame, metres.</param>
/// <param name="DistanceMeters">Length of <paramref name="WorldDelta"/>.</param>
/// <param name="Target">The target.</param>
public readonly record struct NavigationGuidance(Vector3D WorldDelta, double DistanceMeters, NavigationTarget Target);
