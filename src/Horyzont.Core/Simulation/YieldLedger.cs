using Horyzont.Core.Ids;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Server memory of fractional mining yield and of fill overpayment per player and terrain material (K6-29W D10, D12). Lost on restart,
/// as the spec allows. Resolution stages changes; they take effect only through <see cref="Commit"/> after the operation was applied.
/// </summary>
internal sealed class YieldLedger
{
    private readonly Dictionary<(PlayerId Player, byte Material), double> _remainders = [];
    private readonly Dictionary<(PlayerId Player, byte Material), double> _credits = [];
    private readonly List<(byte Material, double Value, bool IsCredit)> _pending = [];
    private PlayerId _pendingPlayer;

    /// <summary>Fractional yield not yet turned into items.</summary>
    internal double Remainder(PlayerId player, byte material) => _remainders.GetValueOrDefault((player, material));

    /// <summary>Items paid for filling but not yet used.</summary>
    internal double Credit(PlayerId player, byte material) => _credits.GetValueOrDefault((player, material));

    /// <summary>Drops the staged changes and starts staging for a player.</summary>
    internal void Begin(PlayerId player)
    {
        _pending.Clear();
        _pendingPlayer = player;
    }

    /// <summary>Stages a new remainder.</summary>
    internal void StageRemainder(byte material, double value) => _pending.Add((material, value, false));

    /// <summary>Stages a new credit.</summary>
    internal void StageCredit(byte material, double value) => _pending.Add((material, value, true));

    /// <summary>Applies the staged changes of the player passed to <see cref="Begin"/>; called after the operation was applied.</summary>
    internal void Commit()
    {
        for (var i = 0; i < _pending.Count; i++)
        {
            var (material, value, isCredit) = _pending[i];
            (isCredit ? _credits : _remainders)[(_pendingPlayer, material)] = value;
        }

        _pending.Clear();
    }
}
