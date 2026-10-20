using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenXt.XArchive;

namespace OpenXt.AssetImport;

/// <summary>
/// Dumps the ship table compiled into the game executable. Unlike meshes and text, these numbers
/// are facts about the game rather than copyrighted assets, so this is how the stats in
/// <c>games/*/data/ships/ships.json</c> are produced and re-checked.
/// </summary>
public static partial class ShipCommands
{
    [GeneratedRegex(@"^t/44\d*\.txt$", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishText { get; }

    public static int Run(XInstall install, bool json)
    {
        IReadOnlyList<ShipRecord> records = ShipTable.Read(install);
        TextTable? names = ReadEnglish(install);

        if (json)
            WriteJson(records, names);
        else
            WriteTable(install, records, names);

        return 0;
    }

    private static TextTable? ReadEnglish(XInstall install)
    {
        using CatArchive archive = install.OpenArchive();
        TextTable? merged = null;

        foreach (CatEntry entry in archive.Entries)
        {
            if (!EnglishText.IsMatch(entry.Path))
                continue;

            TextTable table = TextTable.Parse(archive.Read(entry));
            if (merged is null)
                merged = table;
            else
                merged.Overlay(table);
        }

        return merged;
    }

    private static void WriteTable(XInstall install, IReadOnlyList<ShipRecord> records, TextTable? names)
    {
        Console.WriteLine($"{install.DisplayName}: {records.Count} records from {Path.GetFileName(install.ExePath)}");
        Console.WriteLine();
        Console.WriteLine(" body  text  name                  class   speed   max  accel  power  cargo   max     hull");

        foreach (ShipRecord r in records)
        {
            string name = names?[r.TextId] ?? "";
            if (name.Length > 21)
                name = name[..21];

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{r.BodyId,5} {r.TextId,5}  {name,-21} {r.Class,-6} {r.Speed,6:0.##} {Opt(r.SpeedMax),5} " +
                $"{r.Acceleration,6:0.##} {r.Power,6} {Opt(r.Cargo),6} {Opt(r.CargoMax),5} {Opt(r.Hull),8}"));
        }
    }

    private static string Opt(float? value) =>
        value is { } v ? v.ToString("0.#", CultureInfo.InvariantCulture) : "-";

    private static string Opt(int? value) =>
        value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "-";

    private static void WriteJson(IReadOnlyList<ShipRecord> records, TextTable? names)
    {
        using Stream stdout = Console.OpenStandardOutput();
        using Utf8JsonWriter writer = new(stdout, new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();
        foreach (ShipRecord r in records)
        {
            writer.WriteStartObject();
            writer.WriteNumber("xbtfBodyId", r.BodyId);
            writer.WriteNumber("xbtfTextId", r.TextId);
            if (names?[r.TextId] is { } name)
                writer.WriteString("textName", name);
            writer.WriteString("class", r.Class.ToString());
            writer.WriteNumber("speed", MathF.Round(r.Speed, 2));
            if (r.SpeedMax is { } speedMax)
                writer.WriteNumber("speedMax", MathF.Round(speedMax, 2));
            writer.WriteNumber("acceleration", MathF.Round(r.Acceleration, 2));
            writer.WriteNumber("power", r.Power);

            (float pitch, float yaw, float roll) = r.Steering;
            writer.WriteNumber("steeringPitch", MathF.Round(pitch, 4));
            writer.WriteNumber("steeringYaw", MathF.Round(yaw, 4));
            writer.WriteNumber("steeringRoll", MathF.Round(roll, 4));

            WriteOptional(writer, "rudderTunings", r.RudderTunings);
            WriteOptional(writer, "hull", r.Hull);
            WriteOptional(writer, "cargo", r.Cargo);
            WriteOptional(writer, "cargoMax", r.CargoMax);
            WriteOptional(writer, "laserMounts", r.LaserMounts);
            WriteOptional(writer, "maxLaser", ShipTable.Equipment(ShipTable.Lasers, r.MaxLaser));
            WriteOptional(writer, "shieldMounts", r.ShieldMounts);
            WriteOptional(writer, "maxShield", ShipTable.Equipment(ShipTable.Shields, r.MaxShield));
            WriteOptional(writer, "maxMissile", ShipTable.Equipment(ShipTable.Missiles, r.MaxMissile));
            WriteOptional(writer, "missileCount", r.MissileCount);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is { } v)
            writer.WriteNumber(name, v);
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
            writer.WriteString(name, value);
    }
}
