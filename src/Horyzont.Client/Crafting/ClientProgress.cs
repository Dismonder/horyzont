using Horyzont.Core.Progress;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Crafting;

/// <summary>
/// What the server told the local player about progress (K6-32 D11, message 48): the item types the player discovered and the progress keys of the world.
/// A message replaces both; a disconnect clears them. Main thread only.
/// </summary>
public sealed class ClientProgress
{
    private HashSet<string> _keys = new(StringComparer.Ordinal);

    /// <summary>Raised after a message replaced the state or a disconnect cleared it.</summary>
    public event Action? Changed;

    /// <summary>Gets the item types the player discovered.</summary>
    public ItemTypeBitSet Discovered { get; private set; } = ItemTypeBitSet.Empty;

    /// <summary>Gets the progress keys of the world.</summary>
    public IReadOnlySet<string> WorldKeys => _keys;

    /// <summary>Replaces the state with a message.</summary>
    /// <param name="message">Message 48.</param>
    public void Apply(PlayerProgressState message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Discovered = message.Discovered;
        _keys = new HashSet<string>(message.WorldProgressKeys, StringComparer.Ordinal);
        Changed?.Invoke();
    }

    /// <summary>Forgets everything (disconnect).</summary>
    public void Clear()
    {
        Discovered = ItemTypeBitSet.Empty;
        _keys = new HashSet<string>(StringComparer.Ordinal);
        Changed?.Invoke();
    }
}
