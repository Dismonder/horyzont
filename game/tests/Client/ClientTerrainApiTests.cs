using System.Reflection;

using GdUnit4;

using Godot;

using Horyzont.Game.Boot;
using Horyzont.Game.Client;
using Horyzont.Game.Terrain;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Client;

[TestSuite]
public class ClientTerrainApiTests
{
    private static readonly Type[] _forbidden = [typeof(VoxelLodTerrain), typeof(VoxelTool), typeof(VoxelTerrain), typeof(PlanetTerrain), typeof(VoxelTerrainService)];

    [TestCase]
    public void ClientFacingTerrainApi_ExposesNoMutableTerrain()
    {
        // The client edits terrain only through intents; its terrain types must not hand out the authoritative nodes or tools.
        Type[] surface = [typeof(IClientTerrainProvider), typeof(IClientTerrainView), typeof(SharedTerrainProvider), typeof(OwnTerrainProvider), typeof(ClientNode)];
        var leaks = new List<string>();
        foreach (var type in surface)
        {
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var used in TypesOf(member))
                {
                    if (_forbidden.Any(f => f.IsAssignableFrom(used)))
                    {
                        leaks.Add($"{type.Name}.{member.Name} uses {used.Name}");
                    }
                }
            }
        }

        AssertThat(leaks.Count).IsEqual(0);
        AssertThat(typeof(GameComposition).GetProperty(nameof(GameComposition.ClientTerrain))!.PropertyType).IsEqual(typeof(IClientTerrainProvider));
    }

    private static IEnumerable<Type> TypesOf(MemberInfo member)
    {
        switch (member)
        {
            case PropertyInfo property:
                yield return property.PropertyType;
                break;
            case FieldInfo field:
                yield return field.FieldType;
                break;
            case MethodBase method:
                if (method is MethodInfo info)
                {
                    yield return info.ReturnType;
                }

                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
                }

                break;
            default:
                break;
        }
    }
}
