using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>The tool tier of a player (K6-31 D9): the tier of a tool item in the hotbar, 0 for the bare hand and for items that are no tools.</summary>
internal static class ToolTiers
{
    /// <summary>The tier of one stack: the tier of a tool item, otherwise 0.</summary>
    internal static int OfStack(WorldState state, SurvivalContent content, ItemStack stack)
    {
        if (stack.IsEmpty || !state.ItemTypes.TryGetKey(stack.Type, out var key) || !content.Items.TryGet(key, out var def)
            || (def.Flags & ItemFlags.Tool) == 0)
        {
            return 0;
        }

        return def.ToolTier;
    }

    /// <summary>The highest tier among the tools of the own hotbar (0 without a hotbar), the tier of a player who digs terrain.</summary>
    internal static int Best(WorldState state, SurvivalContent content, PlayerId player)
    {
        if (!state.Containers.TryGetOfPlayer(player, ContainerKind.Hotbar, out var hotbar))
        {
            return 0;
        }

        var best = 0;
        for (var slot = 0; slot < hotbar.Size; slot++)
        {
            best = Math.Max(best, OfStack(state, content, hotbar.GetSlot(slot)));
        }

        return best;
    }
}
