namespace Horyzont.Client;

/// <summary>Readiness of replicated world data for local input and prediction.</summary>
public interface IClientWorldReadiness
{
    /// <summary>Gets a value indicating whether local prediction and input may proceed.</summary>
    bool IsReady { get; }
}
