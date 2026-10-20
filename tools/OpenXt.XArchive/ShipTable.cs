using System.Buffers.Binary;

namespace OpenXt.XArchive;

/// <summary>Ship class as the executable encodes it. <see cref="Unknown"/> covers stations and props.</summary>
public enum ShipClass
{
    Unknown,
    TL,
    TS,
    M0,
    M1,
    M2,
    M3,
    M4,
    M5,
    Pirate,
    Goner,
}

/// <summary>
/// One record of the executable's ship table. Raw values are kept as stored; the decoded
/// properties below are the ones that have been cross-checked or are clearly inferable, and every
/// unit conversion lives here so it cannot drift between callers.
/// </summary>
/// <param name="Offset">File offset of the record, for inspection.</param>
/// <param name="Raw">The record as little-endian int32s: 20 for XBTF, 33 for X-Tension.</param>
public sealed record ShipRecord(XGame Game, long Offset, int[] Raw)
{
    public int BodyId => Raw[0];
    public int TextId => Raw[6];

    public ShipClass Class => Game == XGame.Tension
        ? Raw[5] switch
        {
            0 => ShipClass.TL, 1 => ShipClass.TS, 2 => ShipClass.M0, 3 => ShipClass.M1,
            4 => ShipClass.M2, 5 => ShipClass.M3, 6 => ShipClass.M4, 7 => ShipClass.M5,
            9 => ShipClass.Pirate, 10 => ShipClass.Goner, _ => ShipClass.Unknown,
        }
        // XBTF keeps the class in the high word, one higher than X-Tension, and has no pirate class:
        // its pirates are TS. The low word is 8 for ships and 6 for stations.
        : (Raw[5] >> 16) switch
        {
            1 => ShipClass.TL, 2 => ShipClass.TS, 3 => ShipClass.M0, 4 => ShipClass.M1,
            5 => ShipClass.M2, 6 => ShipClass.M3, 7 => ShipClass.M4, 8 => ShipClass.M5,
            11 => ShipClass.Goner, _ => ShipClass.Unknown,
        };

    /// <summary>
    /// Steering per axis (pitch, yaw, roll), 16.16 fixed point. Fighters sit around 0.2-0.5 and
    /// capital ships around 0.001-0.01; the unit is not established.
    /// </summary>
    public (float Pitch, float Yaw, float Roll) Steering =>
        (Raw[2] / 65536f, Raw[3] / 65536f, Raw[4] / 65536f);

    // X-Tension inserts four fields after the text id, so everything from speed on sits four
    // slots later than in XBTF.
    private int Shift => Game == XGame.Tension ? 4 : 0;

    /// <summary>Base top speed in m/s, as the game displays it.</summary>
    public float Speed => Raw[7 + Shift] / ShipTable.DisplayUnitsPerMetre;

    /// <summary>Acceleration in m/s², as the game displays it.</summary>
    public float Acceleration => Raw[8 + Shift] / ShipTable.DisplayUnitsPerMetre;

    /// <summary>Power generator output in MW: 100 for most ships, 1000 and up for capital ships.</summary>
    public int Power => Raw[13 + Shift];

    // Everything below exists only in X-Tension's longer record; XBTF's 80-byte record ends
    // before it, so for XBTF these are null.

    /// <summary>Engine tunings the ship accepts; each adds 10 % of base speed.</summary>
    public int? EngineTunings => Extended(28);

    /// <summary>Top speed with every engine tuning fitted.</summary>
    public float? SpeedMax => EngineTunings is { } tunings ? Speed * (1f + 0.1f * tunings) : null;

    public int? RudderTunings => Extended(29);

    public int? Cargo => Extended(30);
    public int? CargoMax => Extended(31);

    /// <summary>Inferred: scales from 400 (Discoverer) to 1 000 000 (Argon One).</summary>
    public int? Hull => Extended(8);

    // The most a ship can mount, confirmed against elite-games.ru's "Максимальные лазеры /
    // ракеты / щиты" (e.g. Buster: 2 x gamma PAC, Silkworm, 2 x 5 MW; it is sold with less).
    // Types index the lists in ShipTable.

    /// <summary>Best laser type, alpha IRE (0) to gamma HEPT (8).</summary>
    public int? MaxLaser => Extended(22);
    public int? LaserMounts => Extended(23);

    /// <summary>Best shield type: 0 = 1 MW, 1 = 5 MW, 2 = 25 MW, 3 = 125 MW.</summary>
    public int? MaxShield => Extended(24);
    public int? ShieldMounts => Extended(25);

    /// <summary>Best missile type, -1 for none.</summary>
    public int? MaxMissile => Extended(26);

    /// <summary>Inferred; the database does not list missile counts.</summary>
    public int? MissileCount => Extended(27);

    private int? Extended(int index) => Game == XGame.Tension ? Raw[index] : null;
}

/// <summary>
/// The ship table compiled into <c>X.exe</c> (XBTF) and <c>X-TENSION.exe</c>. The archives carry
/// no ship statistics; this table is where they are.
///
/// Layout, established from the executables and cross-checked against the X-Tension manual's ship
/// database: fixed-size records of little-endian int32s, 80 bytes in XBTF and 132 in X-Tension.
/// Both start with the body id at [0], steering at [2..4], class at [5] and the text id at [6].
/// XBTF continues with speed and acceleration at [7] and [8] and power at [13]; X-Tension inserts
/// four fields (hull among them) and has those at [11], [12] and [17], then adds maximum
/// equipment, tunings and cargo.
///
/// The game indexes the table from the Argon Mammoth record (text id 3161): XBTF's code addresses
/// its fields relative to that record. Earlier records with the same stride are stations. So the
/// reader finds the Mammoth and walks forward while the text id stays in the object range.
/// </summary>
public static class ShipTable
{
    /// <summary>
    /// Stored speed and acceleration units per displayed metre. Calibrated against what the games
    /// show, via elite-games.ru's databases: XBTF's Xperimental, 12 000 raw, displays 54.05 m/s and
    /// 216.21 with its 30 tunings; X-Tension's Discoverer, 18 140, displays 82 and 490; the
    /// Pegasus's acceleration of 50 200 displays 226 m/s². The X-Tension manual prints the raw
    /// numbers and calls them m/s, which they are not.
    /// </summary>
    public const float DisplayUnitsPerMetre = 222f;

    private const int FirstTextId = 3161;
    private const int TextIdOffset = 24;

    // Equipment indices follow the ware order of the text tables (lasers 2613-2693, shields
    // 2713-2743, missiles 2813-2853, ten ids apart), and match the maximum equipment
    // elite-games.ru lists for every X-Tension ship it covers.

    public static readonly string[] Lasers =
    [
        "alpha_ire", "beta_ire", "gamma_ire",
        "alpha_pac", "beta_pac", "gamma_pac",
        "alpha_hept", "beta_hept", "gamma_hept",
    ];

    public static readonly string[] Shields = ["shield_1mw", "shield_5mw", "shield_25mw", "shield_125mw"];

    public static readonly string[] Missiles = ["mosquito", "wasp", "dragonfly", "silkworm", "hornet"];

    /// <summary>The list entry for an equipment index, or null for none (-1) or out of range.</summary>
    public static string? Equipment(string[] list, int? index) =>
        index is { } i && i >= 0 && i < list.Length ? list[i] : null;

    public static int RecordSize(XGame game) => game == XGame.Tension ? 132 : 80;

    public static IReadOnlyList<ShipRecord> Read(XInstall install) =>
        Read(install.Game, File.ReadAllBytes(install.ExePath ??
            throw new FileNotFoundException($"no executable found in '{install.Root}'")));

    public static IReadOnlyList<ShipRecord> Read(XGame game, ReadOnlySpan<byte> exe)
    {
        int stride = RecordSize(game);
        int start = FindStart(exe, stride);
        if (start < 0)
            throw new InvalidDataException("ship table not found in executable");

        List<ShipRecord> records = [];
        for (int offset = start; offset + stride <= exe.Length; offset += stride)
        {
            int[] raw = new int[stride / 4];
            for (int i = 0; i < raw.Length; i++)
                raw[i] = BinaryPrimitives.ReadInt32LittleEndian(exe.Slice(offset + i * 4));

            if (!IsObjectTextId(raw[6]))
                break;

            records.Add(new ShipRecord(game, offset, raw));
        }

        return records;
    }

    /// <summary>
    /// The first int32 3161 that sits at the text-id slot of a record followed by more records.
    /// One neighbour is enough to reject the stray 3161 constants elsewhere in the image.
    /// </summary>
    private static int FindStart(ReadOnlySpan<byte> exe, int stride)
    {
        Span<byte> needle = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(needle, FirstTextId);

        int searched = 0;
        while (searched < exe.Length)
        {
            int hit = exe[searched..].IndexOf(needle);
            if (hit < 0)
                return -1;

            int at = searched + hit;
            int start = at - TextIdOffset;
            int next = at + stride;

            if (start >= 0 && at % 4 == 0 && next + 4 <= exe.Length &&
                IsObjectTextId(BinaryPrimitives.ReadInt32LittleEndian(exe.Slice(next))))
                return start;

            searched = at + 1;
        }

        return -1;
    }

    /// <summary>Ship and object names live at 2000-3999 in the text tables; see <see cref="TextTable"/>.</summary>
    private static bool IsObjectTextId(int id) => id is >= 2000 and < 4000;
}
