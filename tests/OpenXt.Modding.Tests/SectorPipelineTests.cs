using OpenXt.Sim;
using OpenXt.Sim.Data;
using OpenXt.Sim.Modding;
using OpenXt.Sim.Systems;
using Xunit;

namespace OpenXt.Modding.Tests;

/// <summary>
/// The ordered system pipeline a mod registers into: stage first, then declared order, then id.
/// </summary>
public class SectorPipelineTests
{
    private sealed class Recorder(string name, List<string> log) : ISectorSystem
    {
        public void Update(Sector sector, float dt) => log.Add(name);
    }

    private static ShipCatalog OneShip() =>
        ShipCatalog.Create([new ShipDefinition { Id = "ship", Name = "Ship" }]);

    private static GameRuleset Rules() => new() { Id = "test", PlayerShip = "ship" };

    [Fact]
    public void StagesRunInTickOrderRegardlessOfRegistrationOrder()
    {
        List<string> log = [];
        SimRegistry registry = new(OneShip(), Rules());

        registry.AddSectorSystem("d.late", SectorStage.Late, _ => new Recorder("late", log));
        registry.AddSectorSystem("c.post", SectorStage.PostPhysics, _ => new Recorder("post", log));
        registry.AddSectorSystem("a.move", SectorStage.Movement, _ => new Recorder("move", log));
        registry.AddSectorSystem("b.intent", SectorStage.Intent, _ => new Recorder("intent", log));

        using Sector sector = new("test", OneShip(), registry.Build());
        sector.Step(1f / 60f);

        Assert.Equal(["intent", "move", "post", "late"], log);
    }

    [Fact]
    public void OrderThenIdBreaksTiesWithinAStage()
    {
        List<string> log = [];
        SimRegistry registry = new(OneShip(), Rules());

        registry.AddSectorSystem("zzz", SectorStage.Intent, _ => new Recorder("zzz(-10)", log), order: -10);
        registry.AddSectorSystem("bbb", SectorStage.Intent, _ => new Recorder("bbb(0)", log));
        registry.AddSectorSystem("aaa", SectorStage.Intent, _ => new Recorder("aaa(0)", log));

        using Sector sector = new("test", OneShip(), registry.Build());
        sector.Step(1f / 60f);

        Assert.Equal(["zzz(-10)", "aaa(0)", "bbb(0)"], log);
    }

    [Fact]
    public void DuplicateSystemIdIsRejected()
    {
        SimRegistry registry = new(OneShip(), Rules());
        registry.AddSectorSystem("same", SectorStage.Late, _ => new Recorder("a", []));

        Assert.Throws<InvalidOperationException>(
            () => registry.AddSectorSystem("same", SectorStage.Late, _ => new Recorder("b", [])));
    }

    [Fact]
    public void EachSectorGetsItsOwnSystemInstances()
    {
        List<string> log = [];
        SimRegistry registry = new(OneShip(), Rules());
        registry.AddSectorSystem("count", SectorStage.Late, _ => new Recorder("tick", log));

        using Universe universe = new(OneShip(), Rules(), registry.Build());
        universe.CreateSector("one");
        universe.CreateSector("two");
        universe.Step(1f / 60f);

        Assert.Equal(2, log.Count);
    }

    private sealed class DisposableSystem : ISectorSystem, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Update(Sector sector, float dt) { }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void DisposingASectorDisposesItsSystems()
    {
        DisposableSystem system = new();
        SimRegistry registry = new(OneShip(), Rules());
        registry.AddSectorSystem("disposable", SectorStage.Late, _ => system);

        Sector sector = new("test", OneShip(), registry.Build());
        sector.Dispose();

        Assert.True(system.Disposed);
    }
}
