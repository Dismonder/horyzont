namespace Horyzont.Client.Crafting;

/// <summary>One ingredient or fuel of a recipe as the panel shows it: how many the recipe needs and how many the player carries (bag and hotbar).</summary>
/// <param name="ItemKey">Item key.</param>
/// <param name="Need">Items for one piece.</param>
/// <param name="Have">Items in the bag and the hotbar.</param>
public readonly record struct IngredientView(string ItemKey, ushort Need, int Have);
