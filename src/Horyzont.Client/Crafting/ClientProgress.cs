using Horyzont.Core.Progress;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Crafting;

/// <summary>
/// What the server told the local player about progress (K6-32 D11, message 48): the item types the player discovered and the progress keys of the world.
/// A message replaces both; a disconnect clears them. Main thread only.
/// </summary>
public sealed class ClientProgress
{
    /// <summary>Raised after a message replaced the state or a disconnect cleared it.</summary>
    public event Action? Changed;

    /// <summary>Gets the item types the player discovered.</summary>
    public ItemTypeBitSet Discovered => throw new NotImplementedException("K6-32 D2");

    /// <summary>Gets the progress keys of the world.</summary>
    public IReadOnlySet<string> WorldKeys => throw new NotImplementedException("K6-32 D2");

    /// <summary>Replaces the state with a message.</summary>
    /// <param name="message">Message 48.</param>
    public void Apply(PlayerProgressState message)
    {
        Changed?.Invoke();
        throw new NotImplementedException("K6-32 D2");
    }

    /// <summary>Forgets everything (disconnect).</summary>
    public void Clear() => throw new NotImplementedException("K6-32 D2");

}
