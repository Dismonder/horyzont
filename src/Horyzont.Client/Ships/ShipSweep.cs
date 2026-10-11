using Horyzont.Core.Frames;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Client.Ships;

/// <summary>Hull sweep of the predicted ship.</summary>
/// <param name="from">Start pose.</param>
/// <param name="to">End pose.</param>
/// <returns>The first contact.</returns>
public delegate ShipSweepResult ShipSweep(in PoseD from, in PoseD to);
