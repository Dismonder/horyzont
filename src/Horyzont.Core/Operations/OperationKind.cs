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

    /// <summary><see cref="MarkerSetOp"/>.</summary>
    MarkerSet = 22,

    /// <summary><see cref="MarkerRemoveOp"/>.</summary>
    MarkerRemove = 23,

    /// <summary><see cref="ShipFuelAddOp"/>.</summary>
    ShipFuelAdd = 21,
    /// <summary><see cref="CompoundOp"/>.</summary>
    Compound = 30,

    /// <summary><see cref="ItemTransferOp"/>.</summary>
    ItemTransfer = 31,

    /// <summary><see cref="ItemSwapOp"/>.</summary>
    ItemSwap = 32,

    /// <summary><see cref="ItemCreateOp"/>.</summary>
    ItemCreate = 33,

    /// <summary><see cref="ItemDestroyOp"/>.</summary>
    ItemDestroy = 34,

    /// <summary><see cref="ContainerCreateOp"/>.</summary>
    ContainerCreate = 35,

    /// <summary><see cref="ContainerRemoveOp"/>.</summary>
    ContainerRemove = 36,

    /// <summary><see cref="ContainerConfigureOp"/>.</summary>
    ContainerConfigure = 37,

    /// <summary><see cref="PersistentEntityCreateOp"/> (K6-35).</summary>
    PersistentEntityCreate = 38,

    /// <summary><see cref="PersistentEntityRemoveOp"/> (K6-35).</summary>
    PersistentEntityRemove = 39,

    /// <summary><see cref="ItemDiscoverOp"/> (K6-32).</summary>
    ItemDiscover = 43,

    /// <summary><see cref="BlockEntitySetOp"/>.</summary>
    BlockEntitySet = 40,

    /// <summary><see cref="FoodSetOp"/> (K6-33).</summary>
    FoodSet = 41,

    /// <summary><see cref="SuitRefillOp"/> (K6-34).</summary>
    SuitRefill = 42,

    /// <summary><see cref="ProgressKeyGrantOp"/> (K6-32).</summary>
    ProgressKeyGrant = 45,

    /// <summary><see cref="RespawnPointSetOp"/>.</summary>
    RespawnPointSet = 44,

    /// <summary><see cref="WorldSettingsSetOp"/> (K6-35).</summary>
    WorldSettingsSet = 46,

    /// <summary><see cref="PropRegrowOp"/>.</summary>
    PropRegrow = 47,

    /// <summary><see cref="PropHarvestOp"/>.</summary>
    PropHarvest = 48,

    /// <summary><see cref="BlockFunctionStateSetOp"/>.</summary>
    BlockFunctionStateSet = 49,
}
