namespace Horyzont.Core.Progress;

/// <summary>The three food slots of a player (K6-33, D4). The default value is three empty slots.</summary>
/// <param name="Slot0">The first slot.</param>
/// <param name="Slot1">The second slot.</param>
/// <param name="Slot2">The third slot.</param>
public readonly record struct FoodSlots(FoodSlot Slot0, FoodSlot Slot1, FoodSlot Slot2)
{
    /// <summary>Number of slots.</summary>
    public const int Count = 3;

    /// <summary>Gets three empty slots.</summary>
    public static FoodSlots Empty => default;

    /// <summary>Gets a value indicating whether all three slots are empty.</summary>
    public bool IsEmpty => Slot0.IsEmpty && Slot1.IsEmpty && Slot2.IsEmpty;

    /// <summary>Gets a slot by index.</summary>
    /// <param name="index">0 to 2.</param>
    /// <returns>The slot.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0 to 2.</exception>
    public FoodSlot this[int index] => index switch
    {
        0 => Slot0,
        1 => Slot1,
        2 => Slot2,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "A food slot index is 0 to 2."),
    };

    /// <summary>Replaces one slot.</summary>
    /// <param name="index">0 to 2.</param>
    /// <param name="slot">The new slot.</param>
    /// <returns>The slots with <paramref name="slot"/> at <paramref name="index"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0 to 2.</exception>
    public FoodSlots With(int index, FoodSlot slot) => index switch
    {
        0 => this with { Slot0 = slot },
        1 => this with { Slot1 = slot },
        2 => this with { Slot2 = slot },
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "A food slot index is 0 to 2."),
    };
}
