using Horyzont.Core.Ids;

namespace Horyzont.Core.Props;

/// <summary>Layout V1 of the profile <c>core:planet/1</c> (K6-31 D5). Frozen by <c>PropLayoutV1_GoldenDigest</c> once K6-31 A6 is done.</summary>
public sealed class PropLayoutV1 : IPropLayout
{
    /// <inheritdoc />
    public bool TryGetCandidate(ulong seed, PropId id, out PropCandidate candidate)
    {
        // K6-31 A6: XxHash64 rolls and integer value noise.
        _ = (seed, id);
        candidate = default;
        return false;
    }

    /// <inheritdoc />
    public bool Accepts(PropKindV1 kind, float heightOverRadiusMeters)
    {
        // K6-31 A6: the height bands.
        _ = (kind, heightOverRadiusMeters);
        return false;
    }
}
