using System.Diagnostics.CodeAnalysis;

namespace Horyzont.Core.Content;

/// <summary>The item types of the game content, in file order, looked up by key. Built and validated by <see cref="ContentLoader"/>.</summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<string, ItemDef> _byKey;
    private readonly ItemDef[] _items;

    internal ItemCatalog(ItemDef[] items)
    {
        _items = items;
        _byKey = new Dictionary<string, ItemDef>(items.Length, StringComparer.Ordinal);
        foreach (var item in items)
        {
            _byKey.Add(item.Key, item);
        }
    }

    /// <summary>All item types in file order.</summary>
    public IReadOnlyList<ItemDef> Items => _items;

    /// <summary>Default stack limit of a category.</summary>
    /// <param name="category">Category.</param>
    /// <returns>The limit.</returns>
    public static ushort DefaultMaxStack(ItemCategory category) => category switch
    {
        ItemCategory.Resource => 500,
        ItemCategory.Food => 20,
        ItemCategory.Ammo => 100,
        ItemCategory.Placeable => 50,
        ItemCategory.Component => 100,
        _ => 1,
    };

    /// <summary>Looks up an item type.</summary>
    /// <param name="key">Item key.</param>
    /// <param name="item">The item when found.</param>
    /// <returns>True when the key exists.</returns>
    public bool TryGet(string key, [NotNullWhen(true)] out ItemDef? item) => _byKey.TryGetValue(key, out item);

    /// <summary>
    /// Checks the item types stored in a world when it is loaded: a type may be retired (flag <see cref="ItemFlags.Retired"/>) but never
    /// removed from the content.
    /// </summary>
    /// <param name="worldKeys">Keys of the world registry <c>item_types</c>.</param>
    /// <exception cref="InvalidDataException">A world key is missing from the content.</exception>
    public void CheckWorldItemTypes(IEnumerable<string> worldKeys)
    {
        ArgumentNullException.ThrowIfNull(worldKeys);
        foreach (var key in worldKeys)
        {
            if (!_byKey.ContainsKey(key))
            {
                throw new InvalidDataException($"The world stores item type '{key}', which the content removed; retire it with the flag 'retired' instead.");
            }
        }
    }
}
