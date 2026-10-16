using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// <see cref="RestEnvironment.Evaluate"/> (K6-33 D14): the roof (a vertical ray of 16 m from the point finds a construct or the terrain), the heat (a block
/// with <c>Heat</c> within 5 m) and the comfort (the sum of the <c>Comfort</c> of the different block types within 10 m, capped). The world is
/// <see cref="WorldFactory.CreateWithFood"/>: a construct at (0, 4000, 0) of the home planet with a bed (comfort 1) at cell (1,1,0) and a lamp (comfort 1) at
/// (2,1,0); the point is 0.5 m above the middle of the bed (the seat or bed point of D14). The terrain is the sphere of <see cref="FakeTerrainService"/>.
/// Every test that expects a change first evaluates the twin without it.
/// </summary>
public sealed class RestEnvironmentTests
{
    /// <summary>The point of the rest: 0.5 m above the middle of the bed, planet frame.</summary>
    private static readonly Vector3 _point = new(1.5f, 4002f, 0.5f);

    private static readonly CellPos _campfireNear = new(4, 1, 0);

    /// <summary>The point at the height of the block centres: the distances to the centres of the cells in the row of the bed are exact in float.</summary>
    private static readonly Vector3 _level = new(1.5f, 4001.5f, 0.5f);

    private static RestConditions Evaluate(FoodWorld w, PlanetId? planet = null, Vector3? point = null, VitalsRules? rules = null) =>
        RestEnvironment.Evaluate(
            w.State, FoodBlocks.ContentFor(w.State), w.Blocks.Sim.Terrain, planet ?? WorldFactory.HomePlanet, point ?? _point, rules ?? VitalsRules.Default);

    private static void Place(FoodWorld w, CellPos cell, ushort type) => w.Apply(FunctionalBlockFixture.PlaceBlock(cell, type));

    [Fact]
    public void Evaluate_OpenSky_NotRoofed()
    {
        var w = new FoodWorld();

        var open = Evaluate(w);

        Assert.False(open.Roofed);
        Assert.False(open.Heated);

        // The twin of the roof tests: the same point with a block above it.
        Place(w, new CellPos(1, 5, 0), FunctionalBlocks.StoneType);
        Assert.True(Evaluate(w).Roofed);
    }

    [Fact]
    public void Evaluate_ConstructAbove_Roofed()
    {
        var w = new FoodWorld();
        Assert.False(Evaluate(w).Roofed);

        // 15 m above the point (a block at y = 17 over the floor at 4000) is inside the ray of 16 m, 17 m is not.
        Place(w, new CellPos(1, 19, 0), FunctionalBlocks.StoneType);
        Assert.False(Evaluate(w).Roofed);

        Place(w, new CellPos(1, 17, 0), FunctionalBlocks.StoneType);
        Assert.True(Evaluate(w).Roofed);
    }

    [Fact]
    public void Evaluate_ConstructBesideTheRay_NotRoofed()
    {
        var w = new FoodWorld();
        Place(w, new CellPos(1, 5, 0), FunctionalBlocks.StoneType);
        Assert.True(Evaluate(w).Roofed);

        // Only the vertical ray counts (K6-34 replaces it with the full shelter evaluation): the same block one cell aside does not roof the point.
        Assert.False(Evaluate(w, point: new Vector3(3.5f, 4002f, 3.5f)).Roofed);
    }

    [Fact]
    public void Evaluate_TerrainAbove_Roofed()
    {
        var w = new FoodWorld();
        Assert.False(Evaluate(w).Roofed);

        // The fake terrain is a solid sphere: a point inside it is under the terrain, and the ray answers a hit at once. Real terrain with an overhang
        // answers a hit at the distance of the ceiling; both are "terrain within 16 m".
        w.Blocks.Sim.Terrain.SurfaceRadius = 4010f;

        Assert.True(Evaluate(w).Roofed);
    }

    [Fact]
    public void Evaluate_CampfireWithin5m_Heated()
    {
        var w = new FoodWorld();
        Assert.False(Evaluate(w).Heated);

        Place(w, _campfireNear, FoodBlocks.CampfireType);

        Assert.True(Evaluate(w).Heated);
    }

    [Fact]
    public void Evaluate_CampfireAt6m_NotHeated()
    {
        var w = new FoodWorld();
        Place(w, _campfireNear, FoodBlocks.CampfireType);
        Assert.True(Evaluate(w).Heated);

        var far = new FoodWorld();
        Place(far, new CellPos(7, 1, 0), FoodBlocks.CampfireType);

        Assert.False(Evaluate(far).Heated);
    }

    [Fact]
    public void Evaluate_CampfireExactly5m_Heated()
    {
        // The limits are inclusive (distance of the cell centre to the point <= range, like StationLevels): the cell 6 is 5 m away, the cell 7 is 6 m.
        var far = new FoodWorld();
        Place(far, new CellPos(7, 1, 0), FoodBlocks.CampfireType);
        Assert.False(Evaluate(far, point: _level).Heated);

        var w = new FoodWorld();
        Place(w, new CellPos(6, 1, 0), FoodBlocks.CampfireType);

        Assert.True(Evaluate(w, point: _level).Heated);
    }

    [Fact]
    public void Evaluate_ComfortExactly10m_Counted()
    {
        // The cell 12 is 11 m away (bed and lamp only); the cell 11 is exactly 10 m away and counts.
        var far = new FoodWorld();
        Place(far, new CellPos(12, 1, 0), FoodBlocks.CampfireType);
        Assert.Equal(2, Evaluate(far, point: _level).Comfort);

        var w = new FoodWorld();
        Place(w, new CellPos(11, 1, 0), FoodBlocks.CampfireType);

        Assert.Equal(3, Evaluate(w, point: _level).Comfort);
    }

    [Fact]
    public void Evaluate_Warm_DoesNotAllocate()
    {
        // The evaluation runs every 30 ticks for each resting player (ZASADY 6): the set of different comfort types is a stack bitset, not a HashSet.
        var w = new FoodWorld();
        Place(w, _campfireNear, FoodBlocks.CampfireType);
        var content = FoodBlocks.ContentFor(w.State);
        var terrain = w.Blocks.Sim.Terrain;
        var rules = VitalsRules.Default;
        for (var i = 0; i < 50; i++)
        {
            RestEnvironment.Evaluate(w.State, content, terrain, WorldFactory.HomePlanet, _point, rules);
        }

        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 200; i++)
            {
                RestEnvironment.Evaluate(w.State, content, terrain, WorldFactory.HomePlanet, _point, rules);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, smallest);
    }

    [Fact]
    public void Evaluate_ComfortDistinctTypes_Summed()
    {
        var w = new FoodWorld();
        Assert.Equal(2, Evaluate(w).Comfort);

        Place(w, _campfireNear, FoodBlocks.CampfireType);

        Assert.Equal(3, Evaluate(w).Comfort);
    }

    [Fact]
    public void Evaluate_ComfortSameTypeTwice_CountedOnce()
    {
        var w = new FoodWorld();
        Assert.Equal(2, Evaluate(w).Comfort);

        Place(w, new CellPos(5, 1, 0), FunctionalBlocks.LampType);
        Place(w, new CellPos(6, 1, 0), FunctionalBlocks.LampType);

        Assert.Equal(2, Evaluate(w).Comfort);
    }

    [Fact]
    public void Evaluate_ComfortCappedAtMax()
    {
        var w = new FoodWorld();
        Place(w, _campfireNear, FoodBlocks.CampfireType);
        Assert.Equal(3, Evaluate(w).Comfort);

        Assert.Equal(2, Evaluate(w, rules: VitalsRules.Default with { MaxComfort = 2 }).Comfort);
    }

    [Fact]
    public void Evaluate_BlockBeyond10m_Ignored()
    {
        var w = new FoodWorld();
        Place(w, new CellPos(9, 1, 0), FoodBlocks.CampfireType);
        Assert.Equal(3, Evaluate(w).Comfort);

        // Cell 12 is 11 m from the point (centre to centre): out of the comfort range, and out of the heat range too.
        var far = new FoodWorld();
        Place(far, new CellPos(12, 1, 0), FoodBlocks.CampfireType);

        Assert.Equal(2, Evaluate(far).Comfort);
        Assert.False(Evaluate(far).Heated);
    }

    [Fact]
    public void Evaluate_OtherPlanet_Ignored()
    {
        var w = new FoodWorld(secondPlanet: true);
        var other = new EntityId(102);
        w.Apply(new ConstructCreateOp(
            other, FrameRef.Of(WorldFactory.SecondPlanet), new Vector3(0, 4000, 0), Quaternion.Identity,
            new CellPos(0, 0, 0), BlockState.Create(FunctionalBlocks.StoneType, 0)));
        w.Apply(new BlockSetOp(other, _campfireNear, BlockState.Create(FoodBlocks.CampfireType, 0)));

        // The control: standing on the second planet, the campfire there heats; on the home planet, at the same coordinates, it does not.
        Assert.True(Evaluate(w, WorldFactory.SecondPlanet).Heated);
        Assert.False(Evaluate(w, WorldFactory.HomePlanet).Heated);
        Assert.Equal(2, Evaluate(w, WorldFactory.HomePlanet).Comfort);
    }
}
