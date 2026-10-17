using Horyzont.Core.Items;
using Horyzont.Core.World;

namespace Horyzont.Core.Survival;

/// <summary>Pure rules of a death (K6-35): what a penalty moves and when a player is dead.</summary>
public static class DeathRules
{
    private static readonly ContainerKind[] _nothing = [];
    private static readonly ContainerKind[] _bagAndHotbar = [ContainerKind.PlayerBag, ContainerKind.Hotbar];
    private static readonly ContainerKind[] _everything = [ContainerKind.PlayerBag, ContainerKind.Hotbar, ContainerKind.Equipment];

    /// <summary>The kinds of container that move to a tombstone with a penalty, in the order of the plan: the bag, the hotbar, the equipment (D9).</summary>
    /// <param name="penalty">The penalty.</param>
    /// <returns>The kinds; none for the mild penalty.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The penalty is not defined.</exception>
    public static IReadOnlyList<ContainerKind> MovedKinds(DeathPenalty penalty) => penalty switch
    {
        DeathPenalty.Mild => _nothing,
        DeathPenalty.Normal => _bagAndHotbar,
        DeathPenalty.Hard => _everything,
        _ => throw new ArgumentOutOfRangeException(nameof(penalty), penalty, "Unknown death penalty."),
    };

    /// <summary>True when the vitals say the player is dead: health 0. A player without vitals is a fresh body and alive (D7).</summary>
    /// <param name="vitals">The vitals of the player.</param>
    /// <returns>Whether the player waits for the operation of the death.</returns>
    public static bool IsDead(PlayerVitals? vitals) => vitals is { Health: <= 0f };
}
