# OpenXT

A code-first recreation of a classic 3D space sim XBTF/X-Tension from EGOSOFT in C#.

Current state: greenfield. `Program.cs` is still the template `Hello, World!`. Nothing below describes existing code —
it describes the decisions the code should follow.

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

| Concern             | Library               | Notes                                                           |
|---------------------|-----------------------|-----------------------------------------------------------------|
| Entity model        | DefaultEcs            | Structs-as-components, code-only composition                    |
| Physics / collision | BepuPhysics v2        | Use mostly for broadphase + collision queries; flight is custom |
| Debug UI            | ImGui.NET             | Dev tooling only — never the shipping HUD                       |
| Model import        | AssimpNet             | Offline/import-time, not a runtime dependency if avoidable      |
| Serialization       | System.Text.Json      | Source-generated contexts; avoid reflection on hot paths        |
| Audio               | OpenAL (via MonoGame) | Keep behind our own interface so a backend swap stays cheap     |

Deliberately rejected, and why — don't re-litigate without the owner:

- **Stride** — genuinely good and MIT-licensed, and gameplay can be written entirely in code. Rejected because the
  editor-oriented asset pipeline pulls the project back toward editor workflows.
- **Silk.NET / OpenTK / bgfx** — the right answer if we wanted to write the renderer and platform layer ourselves.
  Rejected as a starting point because it front-loads months of non-gameplay work. Silk.NET may still be added later for
  a specific capability (e.g. a Vulkan path) without replacing MonoGame.

## Architecture

Two layers, kept strictly apart:

```
Game (MonoGame entry point, owns the loop and the graphics device)
 ├── Universe      — persistent world state, save/load root
 ├── Sector        — one simulated locale; spatial index lives here
 ├── Ship          — data-driven definitions; no hardcoded stats
 ├── Flight        — custom Newtonian-ish model, NOT Bepu rigid bodies
 ├── AI            — steering, combat, formation
 ├── Economy       — commodities, prices, production
 ├── Mission       — objectives, triggers, scripting-in-C#
 ├── Rendering     — our own renderer modules over MonoGame's device
 └── Networking    — optional, later; do not design around it yet
```

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

- Build: `dotnet build`. Run: `dotnet run`.
- There's no test project yet. When adding one, the simulation layer's headless-ness is what makes it testable — protect
  that property.
- When adding a dependency, state the license in the PR/commit description. Anything not in the OSS list above needs the
  owner's sign-off.