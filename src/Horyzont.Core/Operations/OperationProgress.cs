using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>
/// Reads the progress changes of an operation, also inside a <see cref="CompoundOp"/> (K6-32): every consumer of discoveries and progress keys
/// uses this instead of checking the type of the operation (like <c>OperationTerrain</c> and <c>OperationConstructs</c>).
/// </summary>
public static class OperationProgress
{
    /// <summary>Collects the players that discovered an item type and whether a progress key was granted.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="discoveredBy">Receives each player with a discovery once, in order of first appearance.</param>
    /// <param name="keysChanged">True when the operation grants a progress key.</param>
    public static void Collect(Operation operation, List<PlayerId> discoveredBy, out bool keysChanged)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(discoveredBy);
        discoveredBy.Clear();
        keysChanged = false;
        Visit(operation, discoveredBy, ref keysChanged);
    }

    private static void Visit(Operation operation, List<PlayerId> discoveredBy, ref bool keysChanged)
    {
        switch (operation)
        {
            case ItemDiscoverOp discover:
                if (!discoveredBy.Contains(discover.Player))
                {
                    discoveredBy.Add(discover.Player);
                }

                break;
            case ProgressKeyGrantOp:
                keysChanged = true;
                break;
            case CompoundOp compound:
                var components = compound.Components;
                for (var i = 0; i < components.Count; i++)
                {
                    Visit(components[i], discoveredBy, ref keysChanged);
                }

                break;
        }
    }
}
