using Horyzont.Core.Ids;

namespace Horyzont.Core.Items;

/// <summary>Resolves the key of an item type to the id of the world (the registry of the server, message 49 on the client).</summary>
public interface IItemTypeKeys
{
    /// <summary>Looks up the id of an item key.</summary>
    /// <param name="key">Content key.</param>
    /// <param name="id">The id when found.</param>
    /// <returns>True when the key is known.</returns>
    bool TryGetId(string key, out ItemTypeId id);
}
