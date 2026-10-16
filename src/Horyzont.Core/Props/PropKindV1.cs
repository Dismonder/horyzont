namespace Horyzont.Core.Props;

/// <summary>Kinds of props of layout V1 (K6-31 D5). Slot 0: trees and bushes, slot 1: rocks and crystals.</summary>
public enum PropKindV1 : byte
{
    /// <summary>No prop.</summary>
    None = 0,

    /// <summary>Pine.</summary>
    Pine = 1,

    /// <summary>Tall pine.</summary>
    PineTall = 2,

    /// <summary>Broadleaf tree.</summary>
    Broadleaf = 3,

    /// <summary>Autumn broadleaf tree.</summary>
    BroadleafAutumn = 4,

    /// <summary>Bush.</summary>
    Bush = 5,

    /// <summary>Rock.</summary>
    Rock = 6,

    /// <summary>Boulder.</summary>
    Boulder = 7,

    /// <summary>Crystal cluster.</summary>
    Crystals = 8,
}
