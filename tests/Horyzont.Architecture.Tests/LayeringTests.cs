using System.Xml.Linq;

namespace Horyzont.Architecture.Tests;

public sealed class LayeringTests
{
    public static TheoryData<Type> PureAssemblies => new()
    {
        typeof(Horyzont.Core.AssemblyMarker),
        typeof(Horyzont.Persistence.AssemblyMarker),
        typeof(Horyzont.Net.AssemblyMarker),
        typeof(Horyzont.Constructs.AssemblyMarker),
        typeof(Horyzont.Server.AssemblyMarker),
        typeof(Horyzont.Client.AssemblyMarker),
    };

    [Theory]
    [MemberData(nameof(PureAssemblies))]
    public void PureAssembly_DoesNotReferenceGodot(Type marker)
    {
        var references = marker.Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "");
        Assert.DoesNotContain(references, n => n.StartsWith("Godot", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("src/Horyzont.Core/Horyzont.Core.csproj", "")]
    [InlineData("src/Horyzont.Persistence/Horyzont.Persistence.csproj", "Horyzont.Core")]
    [InlineData("src/Horyzont.Net/Horyzont.Net.csproj", "Horyzont.Core")]
    [InlineData("src/Horyzont.Constructs/Horyzont.Constructs.csproj", "Horyzont.Core")]
    [InlineData("src/Horyzont.Server/Horyzont.Server.csproj", "Horyzont.Core,Horyzont.Persistence,Horyzont.Net")]
    [InlineData("src/Horyzont.Client/Horyzont.Client.csproj", "Horyzont.Core,Horyzont.Net,Horyzont.Constructs")]
    public void ProjectFile_HasOnlyAllowedReferences(string path, string allowedCsv)
    {
        var allowedProjects = allowedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var project = XDocument.Load(Path.Combine(RepoRoot(), path));
        var projectRefs = project.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value));
        var packageRefs = project.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")!.Value);

        Assert.All(projectRefs, r => Assert.Contains(r, allowedProjects));
        Assert.DoesNotContain(packageRefs, p => p.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horyzont.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
