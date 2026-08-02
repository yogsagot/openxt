# OpenXT

A code-first recreation of a classic 3D space sim XBTF/X-Tension from EGOSOFT in C#.

Current state: the skeleton is up and runs. Three projects, the fixed-step loop, the flight model, the Bepu broadphase,
the ImGui debug overlay and the data-driven ship catalog all exist and build; there is no art, no AI, no economy and no
missions yet. Everything below is binding on new code.

## Non-negotiable constraints

These came from the project owner and shape every technical choice:

- **C# for all game logic.** No scripting language layer.
- **No Unity, no Godot.** Unity is not open source; Godot is scene/node-editor centric.
- **No visual editor, no scene graph authoring.** Every entity, system, and world is constructed in code. If a proposal
  starts with "open the editor and drag…", it's wrong for this project.
- **Free and open source dependencies only.** MIT / Apache-2.0 / BSD / MS-PL. Avoid anything with a commercial-use
  royalty or seat fee (this rules out FMOD for anything but an optional, clearly-isolated audio backend).

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
openxt.sln
├── src/OpenXt.Sim              headless simulation — DefaultEcs + BepuPhysics, no MonoGame
│   ├── Universe.cs             persistent world state, save/load root
│   ├── Sector.cs               one simulated locale; owns its ECS world and broadphase
│   ├── FixedStepClock.cs       fixed-timestep accumulator with a spiral-of-death guard
│   ├── Components/             structs only, no behaviour
│   ├── Systems/                FlightSystem; AI, Economy, Mission go here
│   ├── Flight/FlightModel.cs   custom integrator — the feel lives here
│   ├── Physics/                Bepu wrapper + narrow-phase / pose-integrator callbacks
│   └── Data/                   ShipDefinition, ShipCatalog, source-generated JSON context
├── src/OpenXt.Game             window, graphics device, input, render loop
│   ├── OpenXtGame.cs           the only place the two layers meet
│   ├── Rendering/              Camera, DebugShapeRenderer, ImGuiRenderer (our ImGui backend)
│   └── Debug/DebugOverlay.cs   dev overlay, F1
├── tools/OpenXt.AssetImport    offline Assimp importer; nothing else references it
└── data/                       ship catalog and other authored content, copied to output
```

Still to build: AI, Economy, Mission, real meshes and materials, spatial partitioning, save/load,
and networking (optional, later — do not design around it yet).

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
- Inspect a model: `dotnet run --project tools/OpenXt.AssetImport -- path/to/model.obj`.
- There's no test project yet. When adding one, the simulation layer's headless-ness is what makes it testable — protect
  that property. A `Universe` can be stepped in a plain console host with no window; if that ever stops being true,
  something has leaked across the layer boundary.
- When adding a dependency, state the license in the PR/commit description. Anything not in the OSS list above needs the
  owner's sign-off.