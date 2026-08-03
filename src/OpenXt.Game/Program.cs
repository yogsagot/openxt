using OpenXt.Game;
using OpenXt.Modding;
using OpenXt.Sim.Data;
using OpenXt.Sim.Modding;

// The world is built before the window is. Everything up to the point where a GraphicsDevice is
// needed is headless, so a broken package set reports itself on the console and exits instead of
// flashing a window and dying inside the frame loop.

LaunchOptions options = LaunchOptions.Parse(args);

if (options.Error is { } error)
{
    Console.Error.WriteLine($"openxt: {error}");
    Console.Error.WriteLine(LaunchOptions.HelpText);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(LaunchOptions.HelpText);
    return 0;
}

ModHost mods = ModHost.Load(new ModHostOptions
{
    SearchRoots = options.SearchRoots(),
    GameId = options.GameId,
    Disabled = options.Disabled,
    LoadAssemblies = options.LoadAssemblies,
});

if (options.ListMods)
{
    foreach (string line in mods.Describe())
        Console.WriteLine(line);

    if (mods.Diagnostics.Entries.Count > 0)
    {
        Console.WriteLine();
        foreach (ModDiagnostic diagnostic in mods.Diagnostics.Entries)
            Console.WriteLine(diagnostic);
    }

    return mods.IsLoaded ? 0 : 1;
}

foreach (ModDiagnostic diagnostic in mods.Diagnostics.Entries)
    Console.Error.WriteLine($"mods: {diagnostic}");

if (!mods.IsLoaded)
{
    Console.Error.WriteLine("openxt: no game to run. Try --list-mods.");
    return 1;
}

SimWorld world;

try
{
    world = SimBootstrap.Start(mods);
}
catch (ModContentException ex)
{
    Console.Error.WriteLine($"openxt: {ex.Message}");
    return 1;
}

Console.WriteLine($"openxt: {mods.Game.Manifest.DisplayName} — {mods.Packages.Count} package(s), "
                  + $"{world.Universe.Systems.Registrations.Count} sim system(s)");

using OpenXtGame game = new(mods, world);
game.Run();
return 0;
