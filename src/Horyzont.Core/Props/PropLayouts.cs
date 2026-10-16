using System.Diagnostics.CodeAnalysis;

namespace Horyzont.Core.Props;

/// <summary>Maps a generator profile to its prop layout (K6-31 D5).</summary>
public static class PropLayouts
{
    private static readonly PropLayoutV1 _v1 = new();

    /// <summary>Every layout that <see cref="TryGet"/> can return (a test checks them against <see cref="PropHeightLimitsV1"/>).</summary>
    internal static IReadOnlyList<IPropLayout> All { get; } = [_v1];

    /// <summary>Looks up the layout of a generator profile.</summary>
    /// <param name="generatorProfile">Profile key, for example <c>core:planet/1</c>.</param>
    /// <param name="layout">The layout when the profile has one.</param>
    /// <returns>True when the profile has a layout; an unknown profile has no props.</returns>
    public static bool TryGet(string generatorProfile, [NotNullWhen(true)] out IPropLayout? layout)
    {
        ArgumentNullException.ThrowIfNull(generatorProfile);
        if (generatorProfile == "core:planet/1")
        {
            layout = _v1;
            return true;
        }

        layout = null;
        return false;
    }
}
