using System.Buffers.Binary;
using Xunit;

namespace OpenXt.XArchive.Tests;

public sealed class StationTableTests
{
    private static byte[] Image(int stride, IEnumerable<int[]> records, int lead = 64)
    {
        List<int[]> list = [.. records];
        byte[] exe = new byte[lead + list.Count * stride + stride];
        for (int r = 0; r < list.Count; r++)
        for (int i = 0; i < list[r].Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(lead + r * stride + i * 4), list[r][i]);
        return exe;
    }

    private static int[] Xbtf(int body, int kind, int text, int scene)
    {
        int[] raw = new int[20];
        (raw[0], raw[5], raw[6], raw[7], raw[11]) = (body, (kind << 16) | 7, text, 108, scene);
        return raw;
    }

    private static int[] Tension(int body, int kind, int text, int scene, int hull = 0)
    {
        int[] raw = new int[33];
        (raw[0], raw[5], raw[6], raw[8], raw[11], raw[15]) = (body, kind, text, hull, 108, scene);
        return raw;
    }

    [Fact]
    public void XbtfStationsAreTheRecordsBeforeTheShips()
    {
        int[] mammoth = new int[20];
        (mammoth[0], mammoth[6], mammoth[7]) = (102, 3161, 12300);
        int[] elite = new int[20];
        (elite[0], elite[6]) = (0, 3131);

        byte[] exe = Image(80,
        [
            Xbtf(444, 0, 3101, 4035),
            Xbtf(184, 4, 2011, 4031),
            Xbtf(430, 1, 5721, 4024),
            mammoth,
            elite,
        ]);

        IReadOnlyList<StationRecord> stations = StationTable.Read(XGame.BeyondTheFrontier, exe);

        Assert.Equal(3, stations.Count);
        Assert.Null(stations[0].ProductTextId);
        Assert.Equal(4035, stations[0].SceneId);

        Assert.Equal(4, stations[1].Kind);
        Assert.Equal(2013, stations[1].ProductTextId);
        Assert.Null(stations[1].Hull);

        // XBTF files its 5 MW shield factory under 5721; it makes the shield X-Tension calls 2723.
        Assert.Equal(2723, stations[2].ProductTextId);
    }

    [Fact]
    public void XTensionReadsBothStationTables()
    {
        byte[] factories = Image(132,
        [
            Tension(444, 0, 3101, 4035, hull: 10000),
            Tension(184, 4, 2011, 4031, hull: 4000),
        ]);
        byte[] docks = Image(132, [Tension(186, 0, 3181, 4034), Tension(448, 0, 3191, 4089)], lead: 0);

        byte[] exe = [.. factories, .. docks];
        IReadOnlyList<StationRecord> stations = StationTable.Read(XGame.Tension, exe);

        Assert.Equal([3101, 2011, 3181, 3191], stations.Select(s => s.TextId));
        Assert.Equal(4000, stations[1].Hull);
        Assert.Equal(4031, stations[1].SceneId);
    }

    public static TheoryData<string, int> KnownTables => new()
    {
        { "xbtf", 174 },
        { "xtension", 179 + 15 },
    };

    [RequiresInstallTheory]
    [MemberData(nameof(KnownTables))]
    public void ReadsTheRealTables(string gameKey, int count)
    {
        XInstall? install = InstallProbe.ByKey(gameKey);
        if (install is null)
            return;

        IReadOnlyList<StationRecord> stations = StationTable.Read(install);
        Assert.Equal(count, stations.Count);

        StationRecord shipyard = stations[0];
        Assert.Equal(3101, shipyard.TextId);
        Assert.Equal(4035, shipyard.SceneId);

        StationRecord argonSolar = stations.First(s => s.TextId == 2011);
        Assert.Equal(184, argonSolar.BodyId);
        Assert.Equal(4031, argonSolar.SceneId);

        Assert.Contains(WareTable.Read(install), w => w.TextId == 2013);
    }
}
