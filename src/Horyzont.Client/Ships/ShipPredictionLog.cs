using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Ships;

/// <summary>Log messages of the pilot prediction (K5-25).</summary>
internal static partial class ShipPredictionLog
{
    [LoggerMessage(EventId = 2551, Level = LogLevel.Warning,
        Message = "Pilot prediction snapped {Distance:F2} m at input {Seq}; last inputs (thrust Y, newest first): {Inputs}")]
    internal static partial void Snapped(ILogger logger, double distance, uint seq, string inputs);
}
