namespace Horyzont.Core.Operations;

/// <summary>
/// Operation codes. Ranges: 1–7 K1, 8–19 further Planet packages (K2–K4), 20–29 Space, 30–49 Survival, 50–59 Combat,
/// 60–69 EA content.
/// </summary>
public enum OperationKind : byte
{
    /// <summary><see cref="TerrainEditOp"/>.</summary>
    TerrainEdit = 1,

    /// <summary><see cref="ConstructCreateOp"/>.</summary>
    ConstructCreate = 2,

    /// <summary><see cref="BlockSetOp"/>.</summary>
    BlockSet = 3,

    /// <summary><see cref="BlockClearOp"/>.</summary>
    BlockClear = 4,

    /// <summary><see cref="PlayerJoinOp"/>.</summary>
    PlayerJoin = 5,

    /// <summary><see cref="PlayerLeaveOp"/>.</summary>
    PlayerLeave = 6,

    /// <summary><see cref="EntityReparentOp"/>.</summary>
    EntityReparent = 7,

    /// <summary><see cref="ShipModeSetOp"/>.</summary>
    ShipModeSet = 20,

    /// <summary><see cref="ShipFuelAddOp"/>.</summary>
    ShipFuelAdd = 21,
}
