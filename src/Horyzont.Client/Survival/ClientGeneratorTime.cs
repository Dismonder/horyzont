using Horyzont.Client.Blocks;
using Horyzont.Client.Inventory;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.Survival;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Survival;

/// <summary>
/// The time an oxygen generator still runs, for its panel (K6-34 D19): <see cref="OxygenGeneratorProcess.RemainingTicks"/> over the replicated state of the block, the fuel in the input the client
/// follows and the burning time of the content, at the tick of the server. No prediction: the number moves with the tick and jumps with the next state of the block. Main thread only.
/// </summary>
public sealed class ClientGeneratorTime
{
    private readonly ClientInventory _inventory;
    private readonly IBlockEntityView _entities;
    private readonly ulong _ticksPerCell;
    private readonly string? _fuelKey;
    private readonly ItemStack[] _slots = new ItemStack[ProtocolConstants.MaxContainerSlots];

    /// <summary>Initializes a new instance of the <see cref="ClientGeneratorTime"/> class.</summary>
    /// <param name="inventory">The inventory (the fuel is counted in the container of the input).</param>
    /// <param name="entities">The states of the functional blocks.</param>
    /// <param name="hazards">The hazard content (the fuel and the burning time); null when the world has none, then nothing is known.</param>
    /// <param name="tickRate">Ticks per second.</param>
    public ClientGeneratorTime(ClientInventory inventory, IBlockEntityView entities, HazardContent? hazards, int tickRate)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        _fuelKey = hazards?.Generator.Fuel;
        _ticksPerCell = hazards?.Generator.TicksPerCell(tickRate) ?? 0;
    }

    /// <summary>The ticks a generator still runs.</summary>
    /// <param name="construct">The construct of the generator block.</param>
    /// <param name="cell">The cell of the block.</param>
    /// <param name="serverTick">The current tick of the server.</param>
    /// <param name="ticks">The remaining ticks, 0 when it does not run.</param>
    /// <returns>False when the state of the block, its input or the content is not known yet.</returns>
    public bool TryGetRemaining(EntityId construct, CellPos cell, ulong serverTick, out ulong ticks)
    {
        ticks = 0;
        if (_fuelKey is null || _ticksPerCell == 0 || !_entities.TryGet(construct, cell, out var entity) || entity is not OxygenGeneratorBlockState generator
            || !_inventory.TryGetContainer(generator.Input, out var input) || !_inventory.TryGetTypeId(_fuelKey, out var fuel))
        {
            return false;
        }

        var size = Math.Min(input.Size, _slots.Length);
        for (var i = 0; i < size; i++)
        {
            _slots[i] = input.Get(i);
        }

        var cells = OxygenGeneratorProcess.CellsIn(_slots.AsSpan(0, size), fuel);
        ticks = OxygenGeneratorProcess.RemainingTicks(cells, _ticksPerCell, generator.Started, new Tick(serverTick));
        return true;
    }
}
