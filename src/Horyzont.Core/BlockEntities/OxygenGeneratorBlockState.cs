using Horyzont.Core.Ids;

namespace Horyzont.Core.BlockEntities;

/// <summary>State of an oxygen generator (K6-34, D12): its input container and the tick the current fuel cell started burning.</summary>
/// <param name="Input">Input container, kind <see cref="Items.ContainerKind.StationInput"/>, hosted by this block; the generator has no output.</param>
/// <param name="Started">Tick the current cell started at, null when the generator is idle.</param>
public sealed record OxygenGeneratorBlockState(ContainerId Input, Tick? Started) : BlockEntityState
{
    /// <inheritdoc />
    public override BlockEntityKind Kind => BlockEntityKind.OxygenGenerator;
}
