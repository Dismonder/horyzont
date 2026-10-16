using System.Numerics;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>
/// The rules of hitting and harvesting props (K6-31 D9-D11, D24): validation of a <see cref="PropHitIntent"/>, the wear of the prop in
/// <see cref="PropDamageLedger"/> and the harvest as an operation. Reads content, the prop field and the terrain (validator and resolver only,
/// ZASADY section 4); the operation carries the tick of the harvest and every item count. Exists only for content with props (D21).
/// </summary>
internal sealed class PropRules(WorldState state, ITerrainService terrain, SurvivalContent content, GameRules rules, PropField field)
{
    /// <summary>Terrain thinner than this at the end of the sight ray cannot hide the prop (the same slack as the other terrain sights).</summary>
    private const float TerrainSightSlackMeters = 0.25f;
    private const float SightEpsilon = 1e-3f;

    private readonly PropDamageLedger _ledger = new(rules);
    private readonly ItemStack[] _hotbar = new ItemStack[ProtocolConstants.MaxContainerSlots];
    private readonly ItemStack[] _bag = new ItemStack[ProtocolConstants.MaxContainerSlots];
    private readonly List<Operation> _components = [];
    private PlanetId _harvestedPlanet;
    private PropId _harvestedProp;
    private bool _harvestStaged;

    /// <summary>The hit points left of props, for tests.</summary>
    internal PropDamageLedger Ledger => _ledger;

    /// <summary>
    /// The checks of D24 after the general ones of the validator: planet, slot, existence, harvested, reach to the trunk, terrain in between
    /// and the tool tier, in that order.
    /// </summary>
    internal RejectReason Validate(
        PlayerId player, PropHitIntent hit, bool hasPlanet, PlanetId playerPlanet, Vector3 eye, PlayerBuckets buckets, out IntentFacts facts)
    {
        facts = default;
        if (!state.Planets.TryGet(hit.Planet, out _))
        {
            return RejectReason.UnknownPlanet;
        }

        if (!hasPlanet || hit.Planet != playerPlanet)
        {
            return RejectReason.WrongPlanet;
        }

        // A player without a hotbar has only the bare hand; the slot cannot name anything then.
        var hasHotbar = state.Containers.TryGetOfPlayer(player, ContainerKind.Hotbar, out var hotbar);
        if (hasHotbar && hit.ToolSlot >= hotbar!.Size)
        {
            return RejectReason.InvalidParameters;
        }

        // A refused hit does not use a hit token, so what it costs is limited here: an identifier whose anchor cannot be in reach is
        // refused from the hash alone, and a lookup that samples the generator is paid for from its own budget before it happens.
        var cost = field.EstimateLookup(hit.Planet, hit.Prop, eye, rules.ReachMeters);
        if (cost == PropLookupCost.TooFar)
        {
            // Whether the prop exists is not known without sampling, but whatever it is it is not in reach: a far prop is out of reach (a
            // player at the edge of the reach hitting a tree for the first time must be told so), a harvested one is gone.
            return state.HarvestedProps.Contains(hit.Planet, hit.Prop) ? RejectReason.NoHit : RejectReason.OutOfReach;
        }

        if (cost == PropLookupCost.Sampling)
        {
            if (!buckets.PropProbes.HasToken)
            {
                return RejectReason.RateLimited;
            }

            buckets.PropProbes.Consume();
        }

        if (!field.TryGetProp(hit.Planet, hit.Prop, out var prop) || !content.TryGetProp(prop.Kind, out var def))
        {
            return RejectReason.InvalidParameters;
        }

        if (state.HarvestedProps.Contains(hit.Planet, hit.Prop))
        {
            return RejectReason.NoHit;
        }

        var nearest = NearestPointOnTrunk(prop, eye);
        if (Vector3.Distance(eye, nearest) > rules.ReachMeters + prop.BodyRadiusMeters)
        {
            return RejectReason.OutOfReach;
        }

        if (!HasTerrainSight(hit.Planet, eye, nearest))
        {
            return RejectReason.NoLineOfSight;
        }

        if (ToolTierOf(hasHotbar ? hotbar : null, hit.ToolSlot) < def.ToolTier)
        {
            return RejectReason.ToolTierTooLow;
        }

        facts = new IntentFacts(hit.Planet, prop.Anchor, Quaternion.Identity, default);
        return RejectReason.None;
    }

    /// <summary>
    /// Resolves a validated hit. A hit that leaves hit points wears the prop at once and gives no operation (<paramref name="operation"/> is
    /// null); the last hit gives the harvest, which <see cref="Commit"/> turns into a forgotten ledger entry once it was applied.
    /// </summary>
    internal RejectReason Resolve(PlayerId player, PropHitIntent hit, out Operation? operation)
    {
        operation = null;
        _harvestStaged = false;
        if (!field.TryGetProp(hit.Planet, hit.Prop, out var prop) || !content.TryGetProp(prop.Kind, out var def))
        {
            return RejectReason.InvalidParameters;
        }

        var hasHotbar = state.Containers.TryGetOfPlayer(player, ContainerKind.Hotbar, out var hotbar);
        var tier = ToolTierOf(hasHotbar ? hotbar : null, hit.ToolSlot);
        var now = state.CurrentTick;
        var damage = 1 + (tier - def.ToolTier);
        var remaining = _ledger.Remaining(hit.Planet, hit.Prop, now, def.HitPoints);
        if (remaining > damage)
        {
            // Wear is a state of the game, not of the world (D10): no operation, the ledger changes now.
            _ledger.Record(hit.Planet, hit.Prop, now, remaining - damage);
            return RejectReason.None;
        }

        _components.Clear();
        _components.Add(new PropHarvestOp(hit.Planet, hit.Prop, now));
        if (hasHotbar && state.Containers.TryGetOfPlayer(player, ContainerKind.PlayerBag, out var bag))
        {
            var hotbarSlots = Copy(hotbar!, _hotbar);
            var bagSlots = Copy(bag, _bag);
            foreach (var entry in def.Yield)
            {
                // An item the world does not register or the content does not define cannot be created; the harvest still happens (D11).
                if (!state.ItemTypes.TryGetId(entry.ItemKey, out var type) || !content.Items.TryGet(entry.ItemKey, out var item))
                {
                    continue;
                }

                // What does not fit is lost until dropped items exist (D11); the harvest is never blocked.
                ItemPlacement.Create(hotbar!.Id, hotbarSlots, bag.Id, bagSlots, type, entry.Count, item.MaxStack, ItemSource.Harvest, _components);
            }
        }

        operation = _components.Count == 1 ? _components[0] : new CompoundOp(_components.ToArray());
        _harvestedPlanet = hit.Planet;
        _harvestedProp = hit.Prop;
        _harvestStaged = true;
        return RejectReason.None;
    }

    /// <summary>Forgets the prop of the last <see cref="Resolve"/> after its harvest was applied.</summary>
    internal void Commit()
    {
        if (_harvestStaged)
        {
            _ledger.Remove(_harvestedPlanet, _harvestedProp);
            _harvestStaged = false;
        }
    }

    private static Vector3 NearestPointOnTrunk(PropInstance prop, Vector3 eye)
    {
        var height = prop.TrunkHeightMeters;
        var along = Math.Clamp(Vector3.Dot(eye - prop.Anchor, prop.Up), 0f, height);
        return prop.Anchor + (prop.Up * along);
    }

    private bool HasTerrainSight(PlanetId planet, Vector3 eye, Vector3 target)
    {
        var toTarget = target - eye;
        var distance = toTarget.Length();
        if (distance <= SightEpsilon)
        {
            return true;
        }

        return !terrain.Raycast(planet, eye, toTarget / distance, distance, out var hit) || hit.Distance >= distance - TerrainSightSlackMeters;
    }

    private int ToolTierOf(Container? hotbar, int slot) =>
        hotbar is null || slot >= hotbar.Size ? 0 : ToolTiers.OfStack(state, content, hotbar.GetSlot(slot));

    private static Span<ItemStack> Copy(Container container, ItemStack[] scratch)
    {
        var slots = scratch.AsSpan(0, container.Size);
        container.Slots.CopyTo(slots);
        return slots;
    }
}
