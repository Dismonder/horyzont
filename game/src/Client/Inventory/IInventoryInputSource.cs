namespace Horyzont.Game.Client.Inventory;

/// <summary>Where the inventory gets its input: the keyboard and mouse reader, or a script in a test.</summary>
public interface IInventoryInputSource
{
    /// <summary>Takes the input accumulated since the previous call (one call per rendered frame).</summary>
    /// <returns>The frame.</returns>
    InventoryInputFrame Take();
}
