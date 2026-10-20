namespace OpenXt.XArchive;

/// <summary>
/// One station record. Same layout family as <see cref="ShipRecord"/>: body at [0], kind at [5],
/// text id at [6], and in X-Tension four extra fields after the text id, hull among them.
/// </summary>
/// <param name="Offset">File offset of the record, for inspection.</param>
/// <param name="Raw">The record as little-endian int32s: 20 for XBTF, 33 for X-Tension.</param>
public sealed record StationRecord(XGame Game, long Offset, int[] Raw)
{
    public int BodyId => Raw[0];
    public int TextId => Raw[6];

    /// <summary>
    /// 0 = shipyard, dock, trading station or other non-producing station; 1 = high-tech or weapons
    /// factory; 2 = farm or first-tier food; 3 = second-tier food; 4 = power plant; 5 = mine. XBTF
    /// keeps it in the high word, X-Tension in the whole value.
    /// </summary>
    public int Kind => Game == XGame.Tension ? Raw[5] : Raw[5] >> 16;

    private int Shift => Game == XGame.Tension ? 4 : 0;

    /// <summary>
    /// The station's scene: <c>v/0NNNN.pbd</c> in XBTF, <c>cut/0NNNN.pbd</c> in X-Tension. The
    /// multi-part model that places its bodies and lights; 0 for the few records without one.
    /// </summary>
    public int SceneId => Raw[11 + Shift];

    /// <summary>Inferred, X-Tension only: the slot ships use for hull, per variant.</summary>
    public int? Hull => Game == XGame.Tension ? Raw[8] : null;

    /// <summary>
    /// The ware a factory produces: its text id + 2 (Solar Power Plant 2011 makes Energy Cells
    /// 2013). XBTF's 5, 25 and 125 MW shield factories are filed under 5721/5731/5741, ids with
    /// no text; they make the shields X-Tension files under 2723/2733/2743. Null for stations
    /// that produce nothing.
    /// </summary>
    public int? ProductTextId => Kind == 0 ? null : TextId >= 5000 ? TextId - 3000 + 2 : TextId + 2;
}

/// <summary>
/// The station records compiled into the executables. Stations are kept beside the ships:
///
/// * XBTF has one object table of 80-byte records, and its stations are the records before the
///   ship table's Argon Mammoth, back to the Federal Argon Shipyard: 174 of them.
/// * X-Tension has two tables of 132-byte records: factories and shipyards (179, from the Federal
///   Argon Shipyard, text 3101) and trading stations, docks and other non-producing stations (15,
///   from the Free Argon Trading Station, text 3181).
///
/// Every station record has 108 in the slot after its text id (its speed slot, for a ship), which
/// is what separates them from neighbouring data when walking. XBTF's shield factories use text
/// ids 5721-5741, outside the usual object range, so the walk accepts 2000-5999.
/// </summary>
public static class StationTable
{
    private const int StationMarker = 108;

    public static IReadOnlyList<StationRecord> Read(XInstall install) =>
        Read(install.Game, File.ReadAllBytes(install.ExePath ??
            throw new FileNotFoundException($"no executable found in '{install.Root}'")));

    public static IReadOnlyList<StationRecord> Read(XGame game, ReadOnlySpan<byte> exe)
    {
        int stride = ShipTable.RecordSize(game);
        List<StationRecord> records = [];

        if (game == XGame.BeyondTheFrontier)
        {
            int ships = ShipTable.FindStart(game, exe);
            if (ships < 0)
                throw new InvalidDataException("ship table not found in executable");

            int start = ships;
            while (start - stride >= 0 && IsStation(game, ObjectTable.ReadRaw(exe, start - stride, stride)))
                start -= stride;

            Walk(game, exe, start, stride, records);
        }
        else
        {
            foreach (int first in (int[])[3101, 3181])
            {
                int start = ObjectTable.FindRecord(exe, stride, first);
                if (start < 0)
                    throw new InvalidDataException($"station table starting with text id {first} not found");

                Walk(game, exe, start, stride, records);
            }
        }

        return records;
    }

    private static void Walk(XGame game, ReadOnlySpan<byte> exe, int start, int stride, List<StationRecord> into)
    {
        for (int offset = start; offset + stride <= exe.Length; offset += stride)
        {
            int[] raw = ObjectTable.ReadRaw(exe, offset, stride);
            if (!IsStation(game, raw))
                break;

            into.Add(new StationRecord(game, offset, raw));
        }
    }

    private static bool IsStation(XGame game, int[] raw) =>
        raw[6] is >= 2000 and < 6000 && raw[game == XGame.Tension ? 11 : 7] == StationMarker;
}

/// <summary>
/// One ware record: the resources and food the economy trades. Only the text id is decoded; the
/// remaining fields (including what look like model ids) are kept raw.
/// </summary>
public sealed record WareRecord(XGame Game, long Offset, int[] Raw)
{
    public int TextId => Raw[0];
}

/// <summary>
/// The ware records compiled into the executables: energy, minerals and food, the wares factories
/// trade in bulk. Equipment (lasers, shields, missiles) and high-tech goods are not here.
///
/// Records have the ship stride and the text id at [0]. In X-Tension some records are 4 bytes
/// longer than the stride (the first of each group), so the walk tries both steps.
/// </summary>
public static class WareTable
{
    private const int FirstTextId = 2013;

    public static IReadOnlyList<WareRecord> Read(XInstall install) =>
        Read(install.Game, File.ReadAllBytes(install.ExePath ??
            throw new FileNotFoundException($"no executable found in '{install.Root}'")));

    public static IReadOnlyList<WareRecord> Read(XGame game, ReadOnlySpan<byte> exe)
    {
        int stride = ShipTable.RecordSize(game);
        int offset = ObjectTable.FindRecord(exe, stride, FirstTextId, textIdOffset: 0);
        if (offset < 0)
        {
            // X-Tension's first record is followed by its group's 4-byte step.
            offset = ObjectTable.FindRecord(exe, stride + 4, FirstTextId, textIdOffset: 0);
            if (offset < 0)
                throw new InvalidDataException("ware table not found in executable");
        }

        List<WareRecord> records = [];
        while (offset + stride <= exe.Length && IsWare(ObjectTable.ReadInt(exe, offset)))
        {
            records.Add(new WareRecord(game, offset, ObjectTable.ReadRaw(exe, offset, stride)));

            if (IsWare(ObjectTable.ReadInt(exe, offset + stride)))
                offset += stride;
            else if (IsWare(ObjectTable.ReadInt(exe, offset + stride + 4)))
                offset += stride + 4;
            else
                break;
        }

        return records;
    }

    /// <summary>Ware names are the third id of each ten-id block in 2000-2999.</summary>
    private static bool IsWare(int id) => id is >= 2000 and < 3000 && id % 10 == 3;
}
