using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Testing;

/// <summary>Worlds for tests, created only through the public <see cref="WorldState.CreateNew"/>.</summary>
public static class WorldFactory
{
    public const string StoneBlockKey = "core:stone_block";

    public static readonly WorldId DefaultWorldId = new(new Guid("5d1c3f5e-8b7a-4f43-9a51-0d6f7e2c1b10"));

    public static readonly PlanetId HomePlanet = new(1);

    public static PlanetParams HomePlanetParams => new(HomePlanet, new SystemId(1), 7, "core:test/1");

    /// <summary>One planet, block types core:stone_block and core:missing, materials stone and dirt.</summary>
    public static WorldState CreateDefault() => CreateWith(HomePlanetParams);

    /// <summary>Default registries with the given planets.</summary>
    public static WorldState CreateWith(params PlanetParams[] planets) =>
        WorldState.CreateNew(DefaultWorldId, 42, planets, [StoneBlockKey], ["stone", "dirt"]);
}
