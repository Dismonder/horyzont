using Horyzont.Core.Ids;

namespace Horyzont.Core.BlockEntities;

/// <summary>State of a station with time (furnace, refinery; K6-32, D15): its two containers, the chosen recipe and the tick the current piece started.</summary>
/// <param name="Input">Input container, kind <see cref="Items.ContainerKind.StationInput"/>, hosted by this block.</param>
/// <param name="Output">Output container, kind <see cref="Items.ContainerKind.StationOutput"/>, hosted by this block.</param>
/// <param name="Recipe">Key of the chosen recipe, empty for none; at most 64 bytes of UTF-8.</param>
/// <param name="Started">Tick the current piece started, null when the station is idle.</param>
public sealed record StationBlockState(ContainerId Input, ContainerId Output, string Recipe, Tick? Started) : BlockEntityState
{
    /// <inheritdoc />
    public override BlockEntityKind Kind => BlockEntityKind.Station;
}
