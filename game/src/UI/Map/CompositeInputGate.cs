using Horyzont.Client.UI;

namespace Horyzont.Game.UI.Map;

/// <summary>Blocks gameplay input when any of its gates does: the menu of K3-18 and the map of K5-27.</summary>
/// <param name="gates">The gates; null entries are ignored.</param>
public sealed class CompositeInputGate(params IGameplayInputGate?[] gates) : IGameplayInputGate
{
    private readonly IGameplayInputGate?[] _gates = gates;

    /// <inheritdoc />
    public bool IsBlocked
    {
        get
        {
            for (var i = 0; i < _gates.Length; i++)
            {
                if (_gates[i]?.IsBlocked == true)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
