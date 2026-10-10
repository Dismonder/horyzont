namespace Horyzont.Bots.Scripting;

/// <summary>What a bot tells its script about where it is: the way back to the place where it started.</summary>
/// <param name="YawToHome">Yaw (convention of <c>SurfaceFrame</c>) in which home lies.</param>
/// <param name="DistanceHome">Distance to home in metres.</param>
public readonly record struct BotSense(float YawToHome, float DistanceHome);
