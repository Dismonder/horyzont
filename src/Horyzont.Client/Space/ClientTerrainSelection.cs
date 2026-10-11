using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;

namespace Horyzont.Client.Space;

/// <summary>
/// Which planet terrain the client keeps active (K5-22 decision 9, spec 02:132): the body nearest to the camera by <c>r / R</c> once
/// <c>r ≤ 4 R</c>; released beyond <c>4.4 R</c> or when another body wins; the region planet of the own entity is forced first
/// (decision 10). At most one active terrain plus one fading out for <see cref="FadeSeconds"/> (the blend finishes). Before the star
/// system state the caller passes only the home planet. Pure; allocation-free.
/// </summary>
public sealed class ClientTerrainSelection
{
    /// <summary>Distance in radii from which a terrain is activated.</summary>
    public const double ActivateFactor = 4.0;

    /// <summary>Distance in radii beyond which an active terrain is released.</summary>
    public const double ReleaseFactor = 4.4;

    /// <summary>Seconds a released terrain stays for the blend to finish.</summary>
    public const double FadeSeconds = 10.0;

    private double _fadeEnd;

    /// <summary>Gets the active planet; default when none.</summary>
    public PlanetId Active { get; private set; }

    /// <summary>Gets the planet fading out; default when none.</summary>
    public PlanetId Fading { get; private set; }

    /// <summary>Gets a value indicating whether a terrain is active.</summary>
    public bool HasActive => Active != default;

    /// <summary>Gets a value indicating whether a terrain is fading out.</summary>
    public bool HasFading => Fading != default;

    /// <summary>Updates the selection.</summary>
    /// <param name="camera">Camera position in the system frame (world).</param>
    /// <param name="bodies">The bodies (only the home planet before the star system state).</param>
    /// <param name="forced">Region planet of the own entity (forced active), or default.</param>
    /// <param name="nowSeconds">Render time in seconds.</param>
    /// <returns>True when <see cref="Active"/> or <see cref="Fading"/> changed.</returns>
    public bool Update(in Vector3D camera, ReadOnlySpan<BodyInfo> bodies, PlanetId forced, double nowSeconds)
    {
        var active = Active;
        var fading = Fading;
        var candidate = default(PlanetId);
        var best = double.MaxValue;
        var activeRatio = double.MaxValue;
        var forcedKnown = false;
        for (var i = 0; i < bodies.Length; i++)
        {
            var body = bodies[i];
            var ratio = Vector3D.Distance(camera, body.Position) / body.RadiusMeters;
            if (body.Planet == Active)
            {
                activeRatio = ratio;
            }

            forcedKnown |= body.Planet == forced && forced != default;
            if (ratio <= ActivateFactor && ratio < best)
            {
                best = ratio;
                candidate = body.Planet;
            }
        }

        PlanetId next;
        if (forcedKnown)
        {
            next = forced;
        }
        else if (candidate != default && (candidate == Active || best < activeRatio))
        {
            next = candidate;
        }
        else
        {
            next = activeRatio <= ReleaseFactor ? Active : default;
        }

        if (next != Active)
        {
            if (Active != default)
            {
                Fading = Active;
                _fadeEnd = nowSeconds + FadeSeconds;
            }

            Active = next;
        }

        if (Fading != default && (Fading == Active || nowSeconds >= _fadeEnd))
        {
            Fading = default;
        }

        return active != Active || fading != Fading;
    }
}
