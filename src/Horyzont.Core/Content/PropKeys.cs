using Horyzont.Core.Props;

namespace Horyzont.Core.Content;

/// <summary>The keys of the prop kinds of layout V1 as they appear in <c>props/props.json</c> (K6-31 D8).</summary>
internal static class PropKeys
{
    private static readonly string[] _keys =
    [
        string.Empty, "core:pine", "core:pine_tall", "core:broadleaf", "core:broadleaf_autumn", "core:bush", "core:rock", "core:boulder", "core:crystals",
    ];

    /// <summary>The key of a kind.</summary>
    internal static string KeyOf(PropKindV1 kind) => (int)kind > 0 && (int)kind < _keys.Length
        ? _keys[(int)kind]
        : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such prop kind.");

    /// <summary>Finds the kind of a key.</summary>
    internal static bool TryGetKind(string key, out PropKindV1 kind)
    {
        for (var i = 1; i < _keys.Length; i++)
        {
            if (string.Equals(_keys[i], key, StringComparison.Ordinal))
            {
                kind = (PropKindV1)i;
                return true;
            }
        }

        kind = PropKindV1.None;
        return false;
    }
}
