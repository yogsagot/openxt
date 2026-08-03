namespace OpenXt.Modding;

/// <summary>
/// Where packages are looked for, relative to a build.
///
/// There is deliberately no user-data path here. This project must not decide where OpenXT keeps
/// the player's files — that lives in exactly one place (<c>OpenXt.Assets.AssetCachePaths.Root</c>),
/// and the composition root passes it in as an extra search root. Keeping the two apart is what
/// stops the modding layer from growing an opinion about the install layout.
/// </summary>
public static class ModPaths
{
    /// <summary>First-party games, one of which is selected per run.</summary>
    public const string GamesFolder = "games";

    /// <summary>Mods and shared libraries, whether bundled with the build or installed by the player.</summary>
    public const string ModsFolder = "mods";

    /// <summary>The roots that ship with a build, in scan order.</summary>
    public static string[] Bundled(string baseDirectory) =>
    [
        Path.Combine(baseDirectory, GamesFolder),
        Path.Combine(baseDirectory, ModsFolder),
    ];
}
