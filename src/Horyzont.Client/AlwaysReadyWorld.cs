namespace Horyzont.Client;

/// <summary>Neutral K2 world readiness.</summary>
public sealed class AlwaysReadyWorld : IClientWorldReadiness
{
    /// <summary>Gets the stateless default instance.</summary>
    public static AlwaysReadyWorld Instance { get; } = new();

    /// <inheritdoc />
    public bool IsReady => true;
}
