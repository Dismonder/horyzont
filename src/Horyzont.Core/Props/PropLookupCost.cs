namespace Horyzont.Core.Props;

/// <summary>What <see cref="PropField.TryGetProp"/> would cost for an identifier of a client (K6-31 review fix), decided without sampling.</summary>
public enum PropLookupCost
{
    /// <summary>The answer comes without sampling the generated surface: the anchor is cached, or the identifier names no candidate.</summary>
    Cheap,

    /// <summary>The prop would have to be sampled, but its anchor certainly lies out of the reach given.</summary>
    TooFar,

    /// <summary>The prop would have to be sampled and may lie in reach.</summary>
    Sampling,
}
