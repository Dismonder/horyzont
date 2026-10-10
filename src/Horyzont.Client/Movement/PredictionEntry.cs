using Horyzont.Core.Motion;

namespace Horyzont.Client.Movement;

/// <summary>One predicted tick in the <see cref="PredictionBuffer"/>.</summary>
/// <param name="Seq">Number of the input.</param>
/// <param name="Input">The input as the server will see it.</param>
/// <param name="State">The state predicted after the input.</param>
public readonly record struct PredictionEntry(uint Seq, MotorInput Input, PredictedState State);
