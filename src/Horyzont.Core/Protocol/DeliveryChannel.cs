namespace Horyzont.Core.Protocol;

/// <summary>Transport channel of a message.</summary>
public enum DeliveryChannel : byte
{
    /// <summary>Reliable and ordered.</summary>
    ReliableOrdered = 0,

    /// <summary>Unreliable.</summary>
    Unreliable = 1,
}
