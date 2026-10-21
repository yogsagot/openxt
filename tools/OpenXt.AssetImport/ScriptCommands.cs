using OpenXt.XArchive;

namespace OpenXt.AssetImport;

/// <summary>
/// Reads the compiled game script (<c>l/001.obj</c> by default). The full listing runs to hundreds of
/// thousands of lines, so redirect it to a file — outside the repository, since it is the game's code.
/// </summary>
public static class ScriptCommands
{
    public static int Run(XInstall install, string? entryPath, bool summary)
    {
        entryPath ??= "l/001.obj";
        using CatArchive archive = install.OpenArchive();

        if (!archive.TryGetEntry(entryPath, out CatEntry entry))
        {
            Console.Error.WriteLine($"'{entryPath}' is not in {Path.GetFileName(archive.CatPath)}.");
            return 1;
        }

        ScriptFile script = ScriptFile.Parse(archive.Read(entry));

        if (summary)
            return Summary(install, entryPath, script);

        ScriptDisassembler.Write(script, Console.Out);
        return 0;
    }

    private static int Summary(XInstall install, string entryPath, ScriptFile script)
    {
        int instructions = 0, unknown = 0;
        Dictionary<string, int> natives = [];

        foreach (ScriptInstruction instruction in script.Instructions())
        {
            instructions++;
            if (!instruction.IsKnown)
                unknown++;

            if (instruction.Opcode is ScriptOpcode.CallNative or ScriptOpcode.CallNative32
                && script.GetString(instruction.Operand) is { } name)
            {
                natives[name] = natives.GetValueOrDefault(name) + 1;
            }
        }

        Console.WriteLine($"{entryPath}  ({install.DisplayName})");
        Console.WriteLine($"  code words       {script.Code.Count:N0}");
        Console.WriteLine($"  instructions     {instructions:N0}  ({unknown:N0} unknown words)");
        Console.WriteLine($"  global functions {script.Functions.Count:N0}");
        Console.WriteLine($"  global variables {script.Globals.Count:N0}");
        Console.WriteLine($"  classes          {script.Classes.Count:N0}  " +
                          $"({script.Classes.Sum(c => c.Functions.Count):N0} methods)");
        Console.WriteLine($"  natives called   {natives.Count:N0}");

        foreach ((string name, int calls) in natives.OrderByDescending(n => n.Value).ThenBy(n => n.Key, StringComparer.Ordinal))
            Console.WriteLine($"    {calls,7:N0}  {name}");

        return 0;
    }
}
