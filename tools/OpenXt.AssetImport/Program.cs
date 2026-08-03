using System.Globalization;
using OpenXt.Assets;
using OpenXt.AssetImport;
using OpenXt.XArchive;

// Offline asset tooling. Two jobs, kept as separate verbs:
//
//   * archive verbs (ls / cat / verify / import) read the player's own installation of
//     X: Beyond the Frontier or X-Tension and convert it into OpenXT's local cache;
//   * inspect is the generic Assimp-backed mesh dump.
//
// EGOSOFT data is copyrighted. Nothing this tool produces belongs in the repository — the cache
// lives in the user's local application data directory and is regenerable from their own copy.
//
//     dotnet run --project tools/OpenXt.AssetImport -- verify
//     dotnet run --project tools/OpenXt.AssetImport -- import --game xtension

return Cli.Run(args);

internal static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Usage();
            return args.Length == 0 ? 1 : 0;
        }

        string verb = args[0];
        string[] rest = args[1..];

        try
        {
            return verb switch
            {
                "inspect" => InspectCommand.Run(rest),
                "ls" => WithInstall(rest, (install, positional) =>
                    ArchiveCommands.List(install, positional.FirstOrDefault())),
                "cat" => WithInstall(rest, (install, positional) =>
                    positional.Count == 0
                        ? Fail("cat needs an entry path, e.g. 'cat v/00000.pbd'")
                        : ArchiveCommands.Cat(install, positional[0], HasFlag(rest, "--raw"))),
                "verify" => WithInstall(rest, (install, _) => ArchiveCommands.Verify(install)),
                "import" => WithInstall(rest, (install, _) =>
                    ImportCommand.Run(install, HasFlag(rest, "--force"), ReadScale(rest))),
                "meshinfo" => CacheCommands.MeshInfo(ReadOption(rest, "--game") ?? "xbtf", Positionals(rest)),
                "where" => Where(),
                _ => Fail($"unknown verb '{verb}'"),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Resolves which installation to act on, then hands control to the verb along with any
    /// non-flag arguments. Resolution order: --install path, then --game key, then discovery.
    /// </summary>
    private static int WithInstall(string[] args, Func<XInstall, IReadOnlyList<string>, int> action)
    {
        IReadOnlyList<string> positional = Positionals(args);
        string? explicitPath = ReadOption(args, "--install");
        string? game = ReadOption(args, "--game");

        XInstall? install;

        if (explicitPath is not null)
        {
            install = XInstall.Identify(explicitPath);
            if (install is null)
                return Fail($"no 01.cat found in '{explicitPath}'");
        }
        else
        {
            IReadOnlyList<XInstall> found = XInstall.Discover();

            if (game is not null)
            {
                install = found.FirstOrDefault(i =>
                    string.Equals(i.Key, game, StringComparison.OrdinalIgnoreCase));

                if (install is null)
                    return Fail($"no installation matching --game {game}. Try 'where'.");
            }
            else if (found.Count == 0)
            {
                return Fail("no X installation found. Pass --install <path> to point at one.");
            }
            else
            {
                install = found[0];
                if (found.Count > 1)
                    Console.Error.WriteLine($"note: using {install.DisplayName}; use --game to choose.");
            }
        }

        return action(install, positional);
    }

    private static int Where()
    {
        IReadOnlyList<XInstall> found = XInstall.Discover();

        if (found.Count == 0)
        {
            Console.WriteLine("No X installation found in the usual Steam locations.");
            Console.WriteLine("Pass --install <path> to point at one directly.");
            return 1;
        }

        foreach (XInstall install in found)
            Console.WriteLine($"{install.Key,-10} {install.DisplayName,-24} {install.Root}");

        Console.WriteLine();
        Console.WriteLine($"cache: {AssetCachePaths.Root}");
        return 0;
    }

    /// <summary>Options that take a following value, so it is not mistaken for a positional.</summary>
    private static readonly string[] ValueOptions = ["--install", "--game", "--scale"];

    private static bool HasFlag(string[] args, string flag) =>
        args.Contains(flag, StringComparer.Ordinal);

    private static string? ReadOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        }

        return null;
    }

    private static IReadOnlyList<string> Positionals(string[] args)
    {
        List<string> positional = [];

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                // Skip the value that belongs to this option.
                if (ValueOptions.Contains(args[i], StringComparer.Ordinal))
                    i++;
                continue;
            }

            positional.Add(args[i]);
        }

        return positional;
    }

    /// <summary>
    /// --scale overrides metres-per-archive-unit. It exists because that constant is inferred
    /// rather than measured, so correcting it should not need a rebuild.
    /// </summary>
    private static float? ReadScale(string[] args) =>
        ReadOption(args, "--scale") is { } raw
        && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale)
        && scale > 0f
            ? scale
            : null;

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }

    private static void Usage()
    {
        Console.WriteLine("""
            usage: openxt-import <verb> [options]

            Archive verbs (operate on your own X installation):
              where                       list detected installations and the cache location
              ls [prefix]                 list archive contents, optionally filtered by path prefix
              cat <entry> [--raw]         dump one entry; PCK files are decoded unless --raw
              verify                      decode everything and report failures
              import [--force] [--scale N]  convert the archive into OpenXT's local asset cache
              meshinfo <bodyId>...        inspect converted meshes (sizes are in metres)

            Model verb:
              inspect <file>...           dump mesh statistics via Assimp

            Options:
              --install <path>            use this installation directory
              --game <xbtf|xtension>      choose between detected installations
            """);
    }
}
