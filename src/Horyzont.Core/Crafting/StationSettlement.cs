using Horyzont.Core.Ids;
namespace Horyzont.Core.Crafting;

/// <summary>The outcome of <see cref="StationProcess.Settle"/> (K6-32, D15).</summary>
/// <param name="Done">Pieces finished since the last settlement; the caller takes their input from the input container and puts the results in the output.</param>
/// <param name="NextStarted">Tick the next piece started, null when the station is idle after the settlement.</param>
public readonly record struct StationSettlement(int Done, Tick? NextStarted);
