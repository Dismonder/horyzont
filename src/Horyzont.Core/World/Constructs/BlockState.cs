namespace Horyzont.Core.World.Constructs;

/// <summary>
/// Packed state of one construct cell.
/// Bits 0-15: block type id (0 = empty). Bits 16-20: orientation 0-23.
/// Bits 21-24: cost revision 0-15 (refunds use the cost recorded at placement).
/// Bits 25-27: function state 0-7 (meaning defined by the block type in content, opaque to Core).
/// Bits 28-31: reserved, must be 0.
/// </summary>
/// <param name="Packed">Raw 32-bit value.</param>
public readonly record struct BlockState(uint Packed)
{
    /// <summary>Number of orientations.</summary>
    public const int OrientationCount = 24;

    /// <summary>Number of cost revisions.</summary>
    public const int CostRevisionCount = 16;

    /// <summary>Number of function states.</summary>
    public const int FunctionStateCount = 8;

    private const uint FunctionStateMask = 0x7u << 25;

    /// <summary>The empty cell.</summary>
    public static BlockState Empty => default;

    /// <summary>Block type id; 0 for an empty cell.</summary>
    public ushort TypeId => (ushort)(Packed & 0xFFFF);

    /// <summary>Orientation 0-23 (bits 16-20).</summary>
    public byte Orientation => (byte)((Packed >> 16) & 0x1F);

    /// <summary>Cost revision 0-15 (bits 21-24).</summary>
    public byte CostRevision => (byte)((Packed >> 21) & 0xF);

    /// <summary>Function state 0-7 (bits 25-27).</summary>
    public byte FunctionState => (byte)((Packed >> 25) & 0x7);

    /// <summary>True for an empty cell.</summary>
    public bool IsEmpty => TypeId == 0;

    /// <summary>True for the empty value or a non-empty value with a valid orientation and zero reserved bits.</summary>
    public bool IsWellFormed => Packed == 0 || (TypeId != 0 && Orientation < OrientationCount && (Packed >> 28) == 0);

    /// <summary>Packs a non-empty block state.</summary>
    /// <param name="typeId">Block type id, not 0.</param>
    /// <param name="orientation">Orientation below <see cref="OrientationCount"/>.</param>
    /// <param name="costRevision">Cost revision below <see cref="CostRevisionCount"/>.</param>
    /// <param name="functionState">Function state below <see cref="FunctionStateCount"/>.</param>
    /// <returns>The packed state.</returns>
    public static BlockState Create(ushort typeId, byte orientation, byte costRevision = 0, byte functionState = 0)
    {
        if (typeId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(typeId), "Type 0 is the empty block.");
        }

        if (orientation >= OrientationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Orientation must be below 24.");
        }

        if (costRevision >= CostRevisionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(costRevision), costRevision, "Cost revision must be below 16.");
        }

        if (functionState >= FunctionStateCount)
        {
            throw new ArgumentOutOfRangeException(nameof(functionState), functionState, "Function state must be below 8.");
        }

        return new BlockState(typeId | ((uint)orientation << 16) | ((uint)costRevision << 21) | ((uint)functionState << 25));
    }

    /// <summary>Returns the same block with another function state.</summary>
    /// <param name="functionState">Function state below <see cref="FunctionStateCount"/>.</param>
    /// <returns>The changed state.</returns>
    public BlockState WithFunctionState(byte functionState)
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException("An empty cell has no function state.");
        }

        if (functionState >= FunctionStateCount)
        {
            throw new ArgumentOutOfRangeException(nameof(functionState), functionState, "Function state must be below 8.");
        }

        return new BlockState((Packed & ~FunctionStateMask) | ((uint)functionState << 25));
    }
}
