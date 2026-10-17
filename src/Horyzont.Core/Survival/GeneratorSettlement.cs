using Horyzont.Core.Ids;

namespace Horyzont.Core.Survival;

/// <summary>The result of settling an oxygen generator (K6-34, D12).</summary>
/// <param name="Burned">Whole fuel cells burned since the start.</param>
/// <param name="NextStarted">The tick the next piece started at; null when the generator is idle afterwards.</param>
public readonly record struct GeneratorSettlement(int Burned, Tick? NextStarted);
