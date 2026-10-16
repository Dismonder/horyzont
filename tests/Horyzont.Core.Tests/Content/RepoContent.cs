using Horyzont.Core.Content;

namespace Horyzont.Core.Tests.Content;

/// <summary>The survival content of the repository (<c>game/content</c>).</summary>
internal static class RepoContent
{
    /// <summary>Directory of <c>game/content</c>.</summary>
    public static string Directory => Path.Combine(RepoRoot(), "game", "content");

    /// <summary>Loads the three files and, when it exists, <c>props/props.json</c> (K6-31 D8).</summary>
    public static SurvivalContent Load() => ContentLoader.LoadDirectory(Directory);

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
