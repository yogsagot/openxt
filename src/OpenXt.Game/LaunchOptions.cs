using OpenXt.Assets;
using OpenXt.Modding;

namespace OpenXt.Game;

/// <summary>
/// The command line. Small on purpose: everything here is about <i>which</i> world to start, never
/// about how it behaves — that is the ruleset's job, and the ruleset is data.
/// </summary>
public sealed record LaunchOptions
{
    public const string DefaultGameId = "xbtf";

    /// <summary>Which game package to run.</summary>
    public string GameId { get; init; } = DefaultGameId;

    /// <summary>Extra search roots, for testing a package without installing it.</summary>
    public IReadOnlyList<string> ExtraModRoots { get; init; } = [];

    public IReadOnlySet<string> Disabled { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Safe mode: content still layers, package assemblies do not load.</summary>
    public bool LoadAssemblies { get; init; } = true;

    /// <summary>Print the resolved package list and exit without opening a window.</summary>
    public bool ListMods { get; init; }

    public bool ShowHelp { get; init; }

    /// <summary>Set when the arguments were wrong; the message says how.</summary>
    public string? Error { get; init; }

    public static LaunchOptions Parse(string[] args)
    {
        string game = DefaultGameId;
        List<string> roots = [];
        HashSet<string> disabled = new(StringComparer.Ordinal);
        bool assemblies = true;
        bool list = false;
        bool help = false;

        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];

            switch (argument)
            {
                case "--game" or "-g":
                    if (++i == args.Length)
                        return Failed("--game needs a package id.");
                    game = args[i];
                    break;

                case "--mods":
                    if (++i == args.Length)
                        return Failed("--mods needs a directory.");
                    roots.Add(Path.GetFullPath(args[i]));
                    break;

                case "--disable":
                    if (++i == args.Length)
                        return Failed("--disable needs a package id.");
                    foreach (string id in args[i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        disabled.Add(id);
                    break;

                case "--no-plugins":
                    assemblies = false;
                    break;

                case "--list-mods":
                    list = true;
                    break;

                case "--help" or "-h":
                    help = true;
                    break;

                default:
                    return Failed($"unknown argument '{argument}'.");
            }
        }

        return new LaunchOptions
        {
            GameId = game,
            ExtraModRoots = roots,
            Disabled = disabled,
            LoadAssemblies = assemblies,
            ListMods = list,
            ShowHelp = help,
        };

        static LaunchOptions Failed(string message) => new() { Error = message };
    }

    /// <summary>
    /// The search roots for this run: what ships with the build, then the player's own mods
    /// directory, then anything named on the command line. Later wins, so a player-installed
    /// package overrides the bundled copy and a developer's <c>--mods</c> overrides both.
    ///
    /// The user-data location comes from <see cref="AssetCachePaths"/> rather than being
    /// re-derived here — one definition of where OpenXT keeps the player's files.
    /// </summary>
    public IReadOnlyList<(string Root, ModOrigin Origin)> SearchRoots()
    {
        List<(string, ModOrigin)> roots = [.. ModHostOptions.DefaultRoots(AppContext.BaseDirectory)];
        roots.Add((UserModsRoot, ModOrigin.User));

        foreach (string extra in ExtraModRoots)
            roots.Add((extra, ModOrigin.User));

        return roots;
    }

    /// <summary>Where a player installs mods: <c>&lt;asset cache root&gt;/mods</c>.</summary>
    public static string UserModsRoot => Path.Combine(AssetCachePaths.Root, ModPaths.ModsFolder);

    public static string HelpText =>
        """
        OpenXT

          --game, -g <id>     game package to run (default: xbtf)
          --mods <path>       extra package search root; may be repeated
          --disable <ids>     comma-separated package ids to skip
          --no-plugins        load package content but no package code
          --list-mods         print the resolved package list and exit
          --help, -h          this text

        Packages are read from <install>/games, <install>/mods and
        """ + $"{Environment.NewLine}  {UserModsRoot}{Environment.NewLine}";
}
