# Modding OpenXT

OpenXT is an engine with no content of its own. A **game** is a package, a **mod** is a package, and
both are loaded by the same code. X: Beyond the Frontier and X-Tension are two game packages in
`games/`; everything a third party writes goes in `mods/`.

That is the whole design: we are the first consumers of this API, so it cannot quietly stop working.

## Where packages live

```
<install>/games/<id>/            game packages — one is selected per run with --game
<install>/mods/<id>/             packages bundled with the build
~/.local/share/openxt/mods/<id>/ packages the player installed   (%LOCALAPPDATA%\openxt\mods on Windows)
```

Roots are scanned in that order and **a later root wins**: a player-installed package replaces a
bundled one with the same id. `--mods <path>` adds another root, which is the easiest way to test a
package without installing it.

Run `openxt --list-mods` to see exactly what resolved, in order, and why anything was skipped.

## A package

```
mods/my.mod/
    mod.json                     the manifest
    data/                        layered content
        ships/ships.json
        rules/ruleset.json
    MyMod.dll                    optional
```

```json
{
  "id": "my.mod",
  "name": "My Mod",
  "version": "1.0.0",
  "kind": "mod",
  "apiVersion": 1,
  "description": "…",
  "authors": ["…"],
  "license": "MIT",
  "requires": [ { "id": "xbtf", "version": "0.1" } ],
  "loadAfter": ["someone.elses.mod"],
  "assembly": "MyMod.dll",
  "content": "data"
}
```

| Field | Meaning |
|---|---|
| `id` | Lowercase, starting alphanumeric, then letters/digits/`.` `_` `-`. This is the id, not the folder name. |
| `kind` | `game` (a game; one loads per run), `mod` (loads whenever its dependencies resolve), `library` (loads only when something requires it). |
| `apiVersion` | The contract you built against. A package from the future is refused rather than half-loaded. |
| `requires` | Dependencies, each with an optional version range and `"optional": true` to order without gating. |
| `loadAfter` / `loadBefore` | Ordering against packages that may or may not be installed. Not dependencies. |
| `assembly` | Path to your .NET assembly, relative to the package. Omit for a data-only mod. |
| `content` | Your content directory, default `data`. |

Version ranges: `"1.2"` means "at least 1.2.0, below 2.0.0" (the usual case); `">=1.2 <1.5"` when you
know you break on a specific later release; absent means any.

Load order is a topological sort over dependencies and `loadAfter`/`loadBefore`, with ties broken by
id. It is the same on every machine — it has to be, because it decides the order simulation systems
run in.

**Nothing throws over a bad package.** A missing dependency, a wrong version, a broken manifest, a
plugin that throws in its constructor: the package disables itself, the reason appears in
`--list-mods` and in the F1 overlay, and the game still starts.

## Layered content

Every content file is a stack. `data/ships/ships.json` from the game, then from each mod in load
order.

* Files that are one thing (a mesh, a texture) — the last layer wins.
* Catalogs — every layer is **merged**, so two mods can both add ships without either erasing the
  other.

Merge rules:

* Objects merge key by key, recursively. Later scalars win; `null` clears a value.
* Arrays are replaced — **except** arrays whose elements are objects with an `id`, which merge by id.
* Inside a merged array: `"$remove": true` deletes an entry, `"$replace": true` swaps it wholesale
  instead of patching it field by field.

So this changes two numbers on a ship the base game defines, deletes another, and adds a third:

```json
{
  "ships": [
    { "id": "argon_elite", "cruiseSpeed": 168, "mainThrust": 420000 },
    { "id": "teladi_falcon", "$remove": true },
    { "id": "my_courier", "name": "Courier", "mass": 6000, "cruiseSpeed": 190 }
  ]
}
```

Fields you leave out keep their defaults — `my_courier` above gets the default hull radius and
`xbtfBodyId: -1`, meaning "no model", so it draws as a debug shape.

Current content paths:

| Path | What |
|---|---|
| `ships/ships.json` | The ship catalog. |
| `rules/ruleset.json` | The game's rules and start state: asset cache key, start sector, player ship, initial traffic. |

A total conversion is a mod that patches `rules/ruleset.json`.

## Code

A code mod is a .NET class library referencing `OpenXt.Modding` plus whichever layer it extends.
`samples/mods/OpenXt.SampleMod` is a working example and the intended starting point — copy it.

Reference the engine projects (or the shipped assemblies) with **`Private="false"`**. Do not copy
`OpenXt.Sim.dll` and friends next to your mod: the loader resolves them from the host, and a private
copy would give you a second, incompatible set of component types.

The loader instantiates every public type with a parameterless constructor implementing `IPlugin`,
in ordinal order by full name, from the assembly your manifest names. Nothing else is scanned.

### Simulation — `ISimPlugin`

```csharp
public sealed class MyPlugin : ISimPlugin
{
    public void ConfigureSim(ISimRegistry registry) =>
        registry.AddSectorSystem("my.mod.patrol", SectorStage.Intent, sector => new PatrolSystem(sector));
}
```

A tick runs `Intent` → `Movement` → *physics* → `PostPhysics` → `Late`. The physics step is not a
stage anything can register into — mods choose a stage, not a position in the frame.

Inside `ISectorSystem.Update` you are in a deterministic fixed-step loop:

* No wall-clock time, no unseeded `Random`. Two machines must simulate identically.
* No LINQ, no closures, no allocation. Build your `EntitySet` in the constructor — one system
  instance is created per sector.
* Write intent (`FlightControl`), not motion. The flight model is the only thing that moves a ship.

A mod implementing only this never references MonoGame and runs in a headless host.

### Presentation — `IGamePlugin`

```csharp
public sealed class MyPlugin : IGamePlugin
{
    public void ConfigureGame(IGameRegistry registry)
    {
        registry.AddFrameSystem("my.mod.input", new MyInput());
        registry.AddWorldRenderer("my.mod.markers", new MyMarkers());
        registry.AddDebugPanel("my.mod.panel", new MyPanel());
    }
}
```

Configured after the graphics device and asset cache exist, so you may create GPU resources there.
Frame hooks run once per rendered frame at whatever rate the display manages — anything that affects
the world belongs in a sector system instead.

A hook that throws is disabled after its first failure and reported. It costs you the hook, not the
player's session.

## Trust

**A code mod runs with the full rights of the process.** .NET offers no sandbox, and OpenXT does not
pretend otherwise: an assembly loaded here can read your files and open sockets exactly like any
other program you run. What the engine does offer is honesty about it —

* code is opt-in and declared: a package without an `assembly` field never loads any,
* code mods are marked in `--list-mods` and in the F1 overlay,
* `--no-plugins` starts the game with every content layer applied and no third-party code at all,
* `--disable <ids>` skips named packages.

Install code mods from people you would trust with a normal executable, and use `--no-plugins` when
diagnosing a broken install.

## Distributing

Ship the package directory: `mod.json`, your `data/`, and your built assembly plus its `deps.json`
(and any private dependency it genuinely needs — but not the engine's assemblies). Players unpack it
into `~/.local/share/openxt/mods/` (`%LOCALAPPDATA%\openxt\mods` on Windows).

State the game and version you target in `requires`. A mod written for `xbtf` is skipped, with an
explanation, when someone runs `--game xtension`.

## Reference

* Contract version: `ModApi.Version`. Manifests declare it as `apiVersion`.
* CLI: `--game <id>`, `--mods <path>`, `--disable <ids>`, `--no-plugins`, `--list-mods`, `--help`.
* Sample: `samples/mods/OpenXt.SampleMod`.
* First-party packages worth reading: `games/xbtf`, `mods/openxt.ships-core`.
