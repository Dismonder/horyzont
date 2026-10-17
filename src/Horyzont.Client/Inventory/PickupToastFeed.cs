using Horyzont.Core.Ids;

namespace Horyzont.Client.Inventory;

/// <summary>
/// Turns the changes of the owned item counts into toasts (decisions D11 and D19): gains of one type within 1.5 s add up into one toast
/// ("+12 Stone") that stays 3 s, and an accepted dig that gave nothing while the inventory is full shows "Inventory full". Moving items
/// between the bag and the hotbar changes two containers one after the other; a loss and a gain of the same type in the same poll cancel out.
/// </summary>
public sealed class PickupToastFeed
{
    /// <summary>Gains of one type within this time of the first one share a toast.</summary>
    public const long AggregationWindowMs = 1500;

    /// <summary>How long a toast stays.</summary>
    public const long LifetimeMs = 3000;

    /// <summary>A loss and a gain closer than this belong to one move.</summary>
    public const long SamePollMs = 100;

    /// <summary>Time after an accepted dig in which a pickup may still arrive before the dig counts as empty.</summary>
    public const long DigGraceMs = 250;

    private const long Never = -1_000_000_000;

    private readonly List<PickupToast> _active = [];
    private readonly List<(ItemTypeId Type, int Amount, long AtMs)> _credits = [];
    private readonly List<long> _windowStarts = [];
    private long _lastGainMs = Never;
    private long _digAtMs = Never;
    private bool _digPending;
    private bool _digFull;

    /// <summary>Gets the toasts to draw, oldest first.</summary>
    public IReadOnlyList<PickupToast> Active => _active;

    /// <summary>Reports a change of the number of items of a type held by the player.</summary>
    /// <param name="type">Item type.</param>
    /// <param name="delta">Signed change.</param>
    /// <param name="nowMs">Current time in milliseconds.</param>
    public void OnOwnedCountChanged(ItemTypeId type, int delta, long nowMs)
    {
        if (delta < 0)
        {
            var loss = -delta;
            loss -= Retract(type, loss, nowMs);
            if (loss > 0)
            {
                _credits.Add((type, loss, nowMs));
            }

            return;
        }

        if (delta == 0)
        {
            return;
        }

        var gain = delta - UseCredits(type, delta, nowMs);
        if (gain <= 0)
        {
            return;
        }

        _lastGainMs = nowMs;
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var toast = _active[i];
            if (toast.Kind == PickupToastKind.Pickup && toast.Type == type && nowMs - _windowStarts[i] < AggregationWindowMs && nowMs < toast.ExpiresMs)
            {
                _active[i] = toast with { Amount = toast.Amount + gain };
                return;
            }
        }

        _active.Add(new PickupToast(PickupToastKind.Pickup, type, gain, nowMs, nowMs + LifetimeMs));
        _windowStarts.Add(nowMs);
    }

    /// <summary>Reports that the server accepted a dig; if no items follow and the inventory is full, a warning appears.</summary>
    /// <param name="nowMs">Current time in milliseconds.</param>
    /// <param name="inventoryFull">Whether no mined item would find room (<see cref="ClientInventory.HasRoomForResources"/> is false).</param>
    public void OnDigAccepted(long nowMs, bool inventoryFull)
    {
        // The pickup of this dig may have arrived before the reply.
        _digPending = inventoryFull && nowMs - _lastGainMs > SamePollMs;
        _digFull = inventoryFull;
        _digAtMs = nowMs;
    }

    /// <summary>Resolves the pending dig and removes expired toasts; call once per frame.</summary>
    /// <param name="nowMs">Current time in milliseconds.</param>
    public void Update(long nowMs)
    {
        if (_digPending && _digFull)
        {
            if (_lastGainMs >= _digAtMs)
            {
                _digPending = false;
            }
            else if (nowMs - _digAtMs >= DigGraceMs)
            {
                _digPending = false;
                ShowWarning(nowMs);
            }
        }

        for (var i = _active.Count - 1; i >= 0; i--)
        {
            if (nowMs >= _active[i].ExpiresMs)
            {
                _active.RemoveAt(i);
                _windowStarts.RemoveAt(i);
            }
        }

        for (var i = _credits.Count - 1; i >= 0; i--)
        {
            if (nowMs - _credits[i].AtMs > SamePollMs)
            {
                _credits.RemoveAt(i);
            }
        }
    }

    private void ShowWarning(long nowMs)
    {
        for (var i = 0; i < _active.Count; i++)
        {
            if (_active[i].Kind == PickupToastKind.InventoryFull)
            {
                _active[i] = _active[i] with { ExpiresMs = nowMs + LifetimeMs };
                return;
            }
        }

        _active.Add(new PickupToast(PickupToastKind.InventoryFull, default, 0, nowMs, nowMs + LifetimeMs));
        _windowStarts.Add(nowMs);
    }

    /// <summary>A loss right after a gain of the same poll takes the gain back (a move whose destination delta came first).</summary>
    private int Retract(ItemTypeId type, int loss, long nowMs)
    {
        var taken = 0;
        for (var i = _active.Count - 1; i >= 0 && taken < loss; i--)
        {
            var toast = _active[i];
            if (toast.Kind != PickupToastKind.Pickup || toast.Type != type || nowMs - _lastGainMs > SamePollMs)
            {
                continue;
            }

            var take = Math.Min(loss - taken, toast.Amount);
            taken += take;
            if (toast.Amount == take)
            {
                _active.RemoveAt(i);
                _windowStarts.RemoveAt(i);
            }
            else
            {
                _active[i] = toast with { Amount = toast.Amount - take };
            }
        }

        return taken;
    }

    /// <summary>A gain right after a loss of the same poll is the other half of a move.</summary>
    private int UseCredits(ItemTypeId type, int gain, long nowMs)
    {
        var used = 0;
        for (var i = 0; i < _credits.Count && used < gain; i++)
        {
            var credit = _credits[i];
            if (credit.Type != type || nowMs - credit.AtMs > SamePollMs)
            {
                continue;
            }

            var take = Math.Min(gain - used, credit.Amount);
            used += take;
            _credits[i] = credit with { Amount = credit.Amount - take };
        }

        _credits.RemoveAll(c => c.Amount <= 0);
        return used;
    }
}
