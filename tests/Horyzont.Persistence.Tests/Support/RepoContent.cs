using Horyzont.Core.Content;

namespace Horyzont.Persistence.Tests.Support;

/// <summary>The survival content of the repository (<c>game/content</c>), the keys of which the game passes to the loader of a world.</summary>
internal static class RepoContent
{
    /// <summary>Loads the content of the repository, <c>props/props.json</c> included.</summary>
    public static SurvivalContent Load() => ContentLoader.LoadDirectory(Path.Combine(RepoRoot(), "game", "content"));

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
