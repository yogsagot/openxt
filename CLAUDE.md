# OpenXT

A code-first recreation of a classic 3D space sim XBTF/X-Tension from EGOSOFT in C#.

Current state: the skeleton is up and runs, and real ships now render. The fixed-step loop, the flight model, the Bepu
broadphase, the ImGui debug overlay, the data-driven ship catalog and the XBTF/X-Tension asset pipeline all exist and
build; there is no AI, no economy and no missions yet. Everything below is binding on new code.

## Non-negotiable constraints

These came from the project owner and shape every technical choice:

- **C# for all game logic.** No scripting language layer.
- **No Unity, no Godot.** Unity is not open source; Godot is scene/node-editor centric.
- **No visual editor, no scene graph authoring.** Every entity, system, and world is constructed in code. If a proposal
  starts with "open the editor and drag…", it's wrong for this project.
- **Free and open source dependencies only.** MIT / Apache-2.0 / BSD / MS-PL. Avoid anything with a commercial-use
  royalty or seat fee (this rules out FMOD for anything but an optional, clearly-isolated audio backend).
- **EGOSOFT data is copyrighted and never enters this repository.** The law requires the end user to own a legal copy
  of the original game to use its assets. So the player's own installation is the only source, an offline importer
  converts it into a local cache outside the working tree, and the engine must always run without that cache present.
  No extracted mesh, texture, sound or string is ever committed, and no test may depend on one.

## Stack

Framework: **MonoGame** — modern XNA. No editor, no scene graph, no inspectors, no visual scripting. You get windowing,
graphics device, input, audio, and the content pipeline; everything above that is ours. That's the point: an old-school
space sim benefits far more from a hand-written flight model and update loop than from a modern engine's built-in
assumptions.

Chosen libraries:

Versions are centrally managed in `Directory.Packages.props`; projects reference packages without a version.

| Concern             | Library                       | Notes                                                           |
|---------------------|-------------------------------|-----------------------------------------------------------------|
| Framework           | MonoGame.Framework.DesktopGL  | 3.8.5, MIT                                                      |
| Entity model        | DefaultEcs 0.17.2             | Structs-as-components, code-only composition                    |
| Physics / collision | BepuPhysics 2.4.0             | Use mostly for broadphase + collision queries; flight is custom |
| Debug UI            | ImGui.NET 1.91.6.1            | Dev tooling only — never the shipping HUD                       |
| Model import        | AssimpNet 4.1.0               | Offline only, in `tools/` — the game never references it        |
| Serialization       | System.Text.Json              | Source-generated contexts; avoid reflection on hot paths        |
| Audio               | OpenAL (via MonoGame)         | Keep behind our own interface so a backend swap stays cheap     |

Two version pins are load-bearing:

- **ImGui.NET stays at 1.91.6.1.** There is no usable ImGui-for-MonoGame package (the only one on NuGet ships its
  assembly outside `lib/`, so NuGet won't reference it), so `src/OpenXt.Game/Rendering/ImGuiRenderer.cs` is our own
  backend. 1.92 replaced the font-atlas texture API; bumping the pin means rewriting that file.
- **AssimpNet needs `NativeLoaderShim`.** Its last release is from 2019 and it P/Invokes `dlopen` from `libdl.so`,
  which glibc 2.34 folded into libc. `tools/OpenXt.AssetImport/NativeLoaderShim.cs` redirects it. If that package
  becomes more trouble than it's worth, replace it with Silk.NET.Assimp or SharpGLTF — nothing else depends on it.

No MonoGame Content Builder (MGCB). The project is code-first with its own async loading and uses ImGui rather than a
SpriteFont, so nothing needs the content pipeline yet. Add `MonoGame.Content.Builder.Task` when something actually does.

Deliberately rejected, and why — don't re-litigate without the owner:

- **Stride** — genuinely good and MIT-licensed, and gameplay can be written entirely in code. Rejected because the
  editor-oriented asset pipeline pulls the project back toward editor workflows.
- **Silk.NET / OpenTK / bgfx** — the right answer if we wanted to write the renderer and platform layer ourselves.
  Rejected as a starting point because it front-loads months of non-gameplay work. Silk.NET may still be added later for
  a specific capability (e.g. a Vulkan path) without replacing MonoGame.

## Architecture

Two layers, kept strictly apart — and the separation is enforced by the project graph, not by
discipline: `OpenXt.Sim` has no MonoGame reference, so sim code that reaches for a `GraphicsDevice`
does not compile.

```
openxt.slnx
├── src/OpenXt.Sim              headless simulation — DefaultEcs + BepuPhysics, no MonoGame
│   ├── Universe.cs             persistent world state, save/load root
│   ├── Sector.cs               one simulated locale; owns its ECS world and broadphase
│   ├── FixedStepClock.cs       fixed-timestep accumulator with a spiral-of-death guard
│   ├── Components/             structs only, no behaviour
│   ├── Systems/                FlightSystem; AI, Economy, Mission go here
│   ├── Flight/FlightModel.cs   custom integrator — the feel lives here
│   ├── Physics/                Bepu wrapper + narrow-phase / pose-integrator callbacks
│   └── Data/                   ShipDefinition, ShipCatalog, source-generated JSON context
├── src/OpenXt.Assets           the converted-asset format: .oxmesh, cache paths, manifest
│                               shared by the game (reads) and the importer (writes), so the
│                               format cannot drift; no MonoGame, no Assimp
├── src/OpenXt.Game             window, graphics device, input, render loop
│   ├── OpenXtGame.cs           the only place the two layers meet
│   ├── Assets/                 AssetCache (async load, main-thread GPU upload), GpuMesh
│   ├── Rendering/              Camera, MeshRenderer, DebugShapeRenderer, ImGuiRenderer
│   └── Debug/DebugOverlay.cs   dev overlay, F1
├── tools/OpenXt.XArchive       readers for EGOSOFT's .cat/.dat, PCK, body and text formats
│                               format logic only — never any game data
├── tools/OpenXt.AssetImport    the openxt-import CLI; also the offline Assimp inspector
├── tests/OpenXt.XArchive.Tests synthetic fixtures; install-gated tests skip when absent
└── data/                       ship catalog and other authored content, copied to output
```

Still to build: AI, Economy, Mission, scene-graph import (multi-part ships), spatial partitioning,
save/load, and networking (optional, later — do not design around it yet).

## Original game assets

`openxt-import` reads the player's own X: Beyond the Frontier or X-Tension installation and converts it into a cache
under `~/.local/share/openxt` (`%LOCALAPPDATA%` on Windows, overridable with `OPENXT_ASSET_CACHE`). That location is
outside the working tree on purpose — see the copyright constraint above. Both games ship a single `01.cat` index plus
`01.dat` blob; the formats are documented in the XDoc comments on `CatArchive`, `PckStream` and `BodParser`, all of
which were established by decoding the real archives rather than from published specs.

What is imported: models (`v/*.pbd`, text-format bodies with LODs), textures (`tex/true/*.jpg`, copied verbatim since
MonoGame decodes them directly) and language tables (`t/44*.txt` English, `t/49*.txt` German).

Two things to know before touching the pipeline:

- **The archives contain no ship statistics.** XBTF predates the `types/` tables of X2 and later; speed, cargo, shields
  and prices are compiled into `X.exe`. Everything in `data/ships/ships.json` except `xbtfBodyId` and `xbtfTextId` is
  hand-authored by us and tuned by feel. Do not present those numbers as extracted.
- **Scale is an anchor, not a measurement.** `MeshConverter.DefaultMetresPerUnit` is 1/500, which makes the Argon M3
  48 m. Bodies are not authored at a common scale — per-object scale lives in the scene files, which are not imported
  yet — so `openxt-import import --scale N` exists to correct it without a rebuild.

Rules:

- **Simulation must not reference `Microsoft.Xna.Framework.Graphics`.** The sim should be runnable headless. If a system
  needs a `GraphicsDevice`, it's in the wrong layer.
- **Deterministic update loop.** Fixed timestep for simulation, decoupled from render. No `Random` without a seeded,
  per-system instance. No frame-rate-dependent physics.
- **Data-driven content.** Ships, weapons, commodities, star systems come from JSON/data files, not C# literals. Adding
  a ship should never require a code change.
- **Custom flight model.** Bepu handles collision detection and spatial queries; the flight feel (thrust, drag/damping
  curves, assist modes, turret tracking) is ours and stays tunable from data.
- **Spatial queries via octree/BVH**, not linear scans — a star system will hold thousands of entities.
- **Async asset loading.** Never block the update loop on disk or model parsing.

## Conventions

- Target framework is `net10.0`. MonoGame's published support lags .NET releases — if a restore or runtime failure
  traces to that, raise it rather than silently downgrading the target.
- `Nullable` and `ImplicitUsings` are enabled. Keep them enabled; fix nullability warnings rather than suppressing them.
- ECS components are `struct`s with no behavior. Logic belongs in systems.
- Prefer `System.Numerics` vectors/quaternions in the simulation layer so it stays free of MonoGame types; convert at
  the rendering boundary.
- Allocation matters in the per-frame path — no LINQ, no closures, no boxing inside systems that run every tick.
  Elsewhere, write it clearly.

## Working on this project

- Build everything: `dotnet build`.
- Run the game: `dotnet run --project src/OpenXt.Game`.
  Controls: `W/S` thrust, `A/D` strafe, `R/F` lift, arrows pitch/yaw, `Q/E` roll, `F1` overlay, `Esc` quit.
- Run the tests: `dotnet test`. They use synthetic fixtures and pass with no game installed; the tests that need a real
  installation report as skipped.
- Import the original assets (needs your own copy of the game):

  ```
  dotnet run --project tools/OpenXt.AssetImport -- where            # detected installs + cache location
  dotnet run --project tools/OpenXt.AssetImport -- verify           # decode everything, report failures
  dotnet run --project tools/OpenXt.AssetImport -- import           # populate the cache
  dotnet run --project tools/OpenXt.AssetImport -- meshinfo 0       # inspect a converted mesh, sizes in metres
  ```

  Add `--game xbtf|xtension` to choose, or `--install <path>` to point at an installation directly. `ls` and
  `cat <entry>` dump the raw archive. Without a cache the game still runs, on debug shapes.
- Inspect an arbitrary model: `dotnet run --project tools/OpenXt.AssetImport -- inspect path/to/model.obj`.
- The simulation layer's headless-ness is what makes it testable — protect that property. A `Universe` can be stepped in
  a plain console host with no window; if that ever stops being true, something has leaked across the layer boundary.
  The same applies to `OpenXt.XArchive`: it is pure format logic and must stay runnable without a graphics device.
- When adding a dependency, state the license in the PR/commit description. Anything not in the OSS list above needs the
  owner's sign-off.