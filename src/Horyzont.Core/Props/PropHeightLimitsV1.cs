namespace Horyzont.Core.Props;

/// <summary>
/// The widest height range over the radius of a planet in which <see cref="PropLayoutV1"/> accepts any kind (K6-31 review fix). A mirror of the
/// bands of the frozen layout for the code that must bound where a prop can lie without asking the layout; a test keeps the two equal.
/// </summary>
public static class PropHeightLimitsV1
{
    /// <summary>The highest anchor over the radius of the planet, metres.</summary>
    public const float MaxAboveRadiusMeters = 300f;

    /// <summary>The lowest anchor under the radius of the planet, metres (a positive number).</summary>
    public const float MaxBelowRadiusMeters = 300f;
}
