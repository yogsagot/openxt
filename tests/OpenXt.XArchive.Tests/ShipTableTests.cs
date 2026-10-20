using System.Buffers.Binary;
using Xunit;

namespace OpenXt.XArchive.Tests;

public sealed class ShipTableTests
{
    /// <summary>
    /// A fake executable: junk, a station record that must not be picked up, the ship records,
    /// then a record whose text id leaves the object range and ends the table.
    /// </summary>
    private static byte[] Exe(XGame game, params int[][] ships)
    {
        int stride = ShipTable.RecordSize(game);
        int ints = stride / 4;

        List<int[]> records = [Record(ints, body: 449, text: 3291)];
        records.AddRange(ships);
        records.Add(Record(ints, body: 194, text: 770));

        // A stray 3161 in front of the table with nothing table-like after it.
        byte[] exe = new byte[64 + records.Count * stride];
        BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(8), 3161);

        for (int r = 0; r < records.Count; r++)
        for (int i = 0; i < ints; i++)
            BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(64 + r * stride + i * 4), records[r][i]);

        return exe;
    }

    private static int[] Record(int ints, int body, int text)
    {
        int[] raw = new int[ints];
        raw[0] = body;
        raw[6] = text;
        return raw;
    }

    private static int[] Xbtf(int body, int text, int classWord, int speed, int accel)
    {
        int[] raw = Record(20, body, text);
        raw[2] = 16384;
        raw[5] = (classWord << 16) | 8;
        raw[7] = speed;
        raw[8] = accel;
        raw[13] = 100;
        return raw;
    }

    [Fact]
    public void ReadsXbtfRecordsFromTheMammoth()
    {
        byte[] exe = Exe(XGame.BeyondTheFrontier,
            Xbtf(body: 102, text: 3161, classWord: 1, speed: 12300, accel: 1200),
            Xbtf(body: 0, text: 3131, classWord: 6, speed: 15400, accel: 600));

        IReadOnlyList<ShipRecord> ships = ShipTable.Read(XGame.BeyondTheFrontier, exe);

        Assert.Equal(2, ships.Count);
        Assert.Equal(ShipClass.TL, ships[0].Class);

        ShipRecord elite = ships[1];
        Assert.Equal(0, elite.BodyId);
        Assert.Equal(3131, elite.TextId);
        Assert.Equal(ShipClass.M3, elite.Class);
        Assert.Equal(15400f / 222f, elite.Speed);
        Assert.Equal(600f / 222f, elite.Acceleration);
        Assert.Equal(0.25f, elite.Steering.Pitch);
        Assert.Equal(100, elite.Power);

        // XBTF's record stops at the flight data.
        Assert.Null(elite.SpeedMax);
        Assert.Null(elite.Cargo);
        Assert.Null(elite.MaxLaser);
    }

    [Fact]
    public void ReadsXTensionExtendedFields()
    {
        int[] mammoth = Record(33, body: 102, text: 3161);
        int[] elite = Record(33, body: 0, text: 3131);
        elite[5] = 5;
        elite[8] = 12500;
        elite[11] = 15400;
        elite[12] = 1600;
        elite[17] = 100;
        (elite[22], elite[23], elite[24], elite[25], elite[26], elite[27]) = (6, 2, 2, 2, 4, 9);
        (elite[28], elite[29], elite[30], elite[31]) = (8, 30, 20, 200);

        ShipRecord ship = ShipTable.Read(XGame.Tension, Exe(XGame.Tension, mammoth, elite))[1];

        Assert.Equal(ShipClass.M3, ship.Class);
        Assert.Equal(15400f / 222f, ship.Speed);
        Assert.Equal(1600f / 222f, ship.Acceleration);
        Assert.Equal(15400f / 222f * 1.8f, ship.SpeedMax!.Value, 3);
        Assert.Equal(100, ship.Power);
        Assert.Equal(20, ship.Cargo);
        Assert.Equal(200, ship.CargoMax);
        Assert.Equal(12500, ship.Hull);
        Assert.Equal(2, ship.LaserMounts);
        Assert.Equal("alpha_hept", ShipTable.Equipment(ShipTable.Lasers, ship.MaxLaser));
        Assert.Equal("shield_25mw", ShipTable.Equipment(ShipTable.Shields, ship.MaxShield));
        Assert.Equal("hornet", ShipTable.Equipment(ShipTable.Missiles, ship.MaxMissile));
    }

    [Fact]
    public void EquipmentIndexMinusOneMeansNone()
    {
        Assert.Null(ShipTable.Equipment(ShipTable.Missiles, -1));
        Assert.Null(ShipTable.Equipment(ShipTable.Missiles, null));
    }

    [Fact]
    public void ThrowsWhenThereIsNoTable()
    {
        Assert.Throws<InvalidDataException>(() => ShipTable.Read(XGame.Tension, new byte[4096]));
    }

    public static TheoryData<string, int> KnownTables => new()
    {
        // game key, records from the Argon Mammoth to the end of the table
        { "xbtf", 66 },
        { "xtension", 71 },
    };

    [RequiresInstallTheory]
    [MemberData(nameof(KnownTables))]
    public void ReadsTheRealTable(string gameKey, int count)
    {
        XInstall? install = InstallProbe.ByKey(gameKey);
        if (install is null)
            return;

        IReadOnlyList<ShipRecord> ships = ShipTable.Read(install);
        Assert.Equal(count, ships.Count);

        ShipRecord elite = ships.Single(s => s.TextId == 3131);
        Assert.Equal(0, elite.BodyId);
        Assert.Equal(ShipClass.M3, elite.Class);
        Assert.Equal(69.37f, elite.Speed, 2);
        Assert.Equal(100, elite.Power);

        // Cross-checked against elite-games.ru: the Buster shows 55 m/s, 143 with 16 tunings.
        ShipRecord buster = ships.Single(s => s.TextId == 3141);
        Assert.Equal(19, buster.BodyId);
        Assert.Equal(55.05f, buster.Speed, 2);
        if (install.Game == XGame.Tension)
        {
            Assert.Equal(16, buster.EngineTunings);
            Assert.Equal(143.1f, buster.SpeedMax!.Value, 1);
        }
    }
}
