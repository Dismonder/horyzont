using Horyzont.Core.Content;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Tests.Props;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Content;

/// <summary>
/// The acceptance criterion of the spec for K6-31: the yield equals the data. Both tests run the repository content (<c>game/content</c>)
/// through the real simulation, digging every terrain material and harvesting every kind of prop of layout V1.
/// </summary>
public sealed class YieldDataTests
{
    private const string Multitool = "core:multitool";
    private const int Voxels = 10;

    private static PropWorldFixture Fixture(SurvivalContent content, IReadOnlyList<string> materials) =>
        new(content: content, state: WorldFactory.CreateForContent(content, materials), toolKey: Multitool);

    [Fact]
    public void Dig_EveryRepoMaterial_YieldMatchesItemsPerCubicMeter()
    {
        var content = RepoContent.Load();
        var materials = content.Yields.Select(y => y.MaterialKey).ToList();
        Assert.NotEmpty(materials);

        for (var i = 0; i < materials.Count; i++)
        {
            var yield = content.Yields[i];
            var f = Fixture(content, materials);
            f.Terrain.MeasureResult = MeasuredSphere.SolidOf((byte)(i + 1), Voxels);

            var result = f.DigGround();

            // A voxel is 1 m³ on the test planet; the stack limit is far above the amount.
            Assert.True(result.IsAccepted, $"{yield.MaterialKey}: {result.Reason}");
            var compound = Assert.IsType<CompoundOp>(result.Applied!.Operation);
            var created = compound.Components.OfType<ItemCreateOp>().ToArray();
            var expected = (int)Math.Floor(Voxels * yield.ItemsPerCubicMeter);
            Assert.All(created, c =>
            {
                Assert.Equal(f.Item(yield.ItemKey), c.Type);
                Assert.Equal(ItemSource.Harvest, c.Source);
            });
            Assert.Equal(expected, created.Sum(c => c.Count));
            Assert.Equal(expected, f.Count(yield.ItemKey));
        }
    }

    [Fact]
    public void HarvestEveryRepoKind_YieldMatchesPropsJson()
    {
        var content = RepoContent.Load();
        var materials = content.Yields.Select(y => y.MaterialKey).ToList();
        var kinds = Enum.GetValues<PropKindV1>().Where(k => k != PropKindV1.None).ToArray();
        Assert.Equal(kinds.Length, content.Props.Count);

        foreach (var kind in kinds)
        {
            Assert.True(content.TryGetProp(kind, out var def), $"{kind} has no definition in game/content/props/props.json.");
            Assert.True(def.ToolTier <= 1, $"{def.Key} needs tier {def.ToolTier}; this test hits with the multitool (tier 1): give it the right tool.");
            var f = Fixture(content, materials);
            var prop = f.FindProp(kind);
            f.StandBeside(prop, 3f);

            // Damage is 1 + (tool tier - needed tier); the last hit harvests.
            var damage = 1 + (1 - def.ToolTier);
            var hits = (def.HitPoints + damage - 1) / damage;
            for (var hit = 1; hit < hits; hit++)
            {
                var wear = f.Hit(prop.Id);
                Assert.True(wear.IsAccepted && wear.Applied is null, $"{def.Key} hit {hit} of {hits}: {wear.Reason}");
            }

            var result = f.Hit(prop.Id);

            Assert.True(result.IsAccepted, $"{def.Key}: {result.Reason}");
            Assert.True(f.State.HarvestedProps.Contains(PropWorldFixture.Planet, prop.Id));
            var components = result.Applied!.Operation is CompoundOp compound ? compound.Components.ToArray() : [result.Applied.Operation];
            var created = components.OfType<ItemCreateOp>().ToArray();
            foreach (var entry in def.Yield)
            {
                Assert.Equal(entry.Count, created.Where(c => c.Type == f.Item(entry.ItemKey)).Sum(c => c.Count));
            }

            Assert.Equal(def.Yield.Sum(y => y.Count), created.Sum(c => c.Count));
            Assert.Equal(def.Yield.Select(y => y.ItemKey).Distinct().Count(), created.Select(c => c.Type).Distinct().Count());
        }
    }
}
