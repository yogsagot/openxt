using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenXt.Game.Assets;
using OpenXt.Game.Debug;
using OpenXt.Game.Modding;
using OpenXt.Game.Rendering;
using OpenXt.Modding;
using OpenXt.Sim;
using OpenXt.Sim.Components;
using OpenXt.Sim.Modding;

// Inside this project, unqualified Vector3/Quaternion/Matrix are MonoGame's. Simulation vectors are
// System.Numerics and stay explicitly named — the conversion happens only at the render boundary.
using SimVector3 = System.Numerics.Vector3;

namespace OpenXt.Game;

/// <summary>
/// The MonoGame entry point. It owns the window, the graphics device and the frame loop, and
/// nothing else — all world state lives in <see cref="Universe"/>, which knows nothing about it.
///
/// It owns no content either: the world it draws was built from the loaded packages before this
/// type existed, and which game that is came from a package too.
/// </summary>
public sealed class OpenXtGame : Microsoft.Xna.Framework.Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly FixedStepClock _clock = new(stepSeconds: 1f / 60f);

    private readonly ModHost _mods;
    private readonly Universe _universe;
    private readonly Sector _sector;
    private readonly Entity _player;

    private EntitySet _renderable = null!;

    private DebugShapeRenderer _shapes = null!;
    private MeshRenderer _meshes = null!;
    private AssetCache _assets = null!;
    private ImGuiRenderer _imgui = null!;
    private DebugOverlay _overlay = null!;
    private GameContext _context = null!;
    private GameRegistry _plugins = null!;
    private Camera _camera = new();

    public OpenXtGame(ModHost mods, SimWorld world)
    {
        _mods = mods;
        _universe = world.Universe;
        _sector = world.StartSector;
        _player = world.Player;

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1600,
            PreferredBackBufferHeight = 900,
            SynchronizeWithVerticalRetrace = true,
            GraphicsProfile = GraphicsProfile.HiDef,
        };

        // We run our own fixed-timestep accumulator; MonoGame's is not used.
        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = $"OpenXT — {_universe.Rules.DisplayTitle}";
    }

    protected override void Initialize()
    {
        // Built once. Rebuilding an EntitySet per frame would allocate and leak subscriptions.
        _renderable = _sector.World.GetEntities().With<Pose>().With<Collider>().AsSet();

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _shapes = new DebugShapeRenderer(GraphicsDevice);
        _meshes = new MeshRenderer(GraphicsDevice);
        _imgui = new ImGuiRenderer(this);
        _overlay = new DebugOverlay();

        // Converted from the player's own installation by `openxt-import import`. Which cache to
        // open is the running game's business, not the engine's — hence the ruleset, not a literal.
        // Absent is a perfectly normal state: the game falls back to debug shapes and says why.
        _assets = new AssetCache(GraphicsDevice, _universe.Rules.AssetKey);
        if (_assets.Problem is { } problem)
            Console.Error.WriteLine($"assets: {problem}");

        _context = new GameContext
        {
            GraphicsDevice = GraphicsDevice,
            Host = this,
            Mods = _mods,
            Universe = _universe,
            Assets = _assets,
            Sector = _sector,
            Player = _player,
        };

        // Plugins configure last: the device, the renderers and the asset cache all exist, so a
        // package may create GPU resources in ConfigureGame.
        _plugins = GameRegistry.Configure(_context);

        // Says on the console what the F1 overlay shows in full. Without it, "did my plugin's
        // ConfigureGame actually run" is only answerable by looking at the window.
        Console.WriteLine($"mods: {_plugins.HookCount} frame hook(s) from {_mods.Plugins.Count} plugin(s)");
    }

    protected override void Update(GameTime gameTime)
    {
        // Only act on input when this window has focus. MonoGame's Keyboard/Mouse report the
        // desktop's global state, so without this the game quits on an Escape pressed in another
        // application and flies itself on someone else's arrow keys.
        if (IsActive && Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        _context.IsFocused = IsActive;

        ReadFlightInput();

        int steps = _clock.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);
        for (int i = 0; i < steps; i++)
            _universe.Step(_clock.Step);

        // Drains finished background loads onto the GPU. Must be the main thread.
        _assets.Update();

        UpdateCamera();

        // Frame-rate work only: anything that changes the world belongs in a sector system.
        _plugins.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

        base.Update(gameTime);
    }

    /// <summary>
    /// Translates the keyboard into normalised flight intent. Input never touches
    /// <see cref="Motion"/> directly — the flight model is the only thing that moves a ship.
    /// </summary>
    private void ReadFlightInput()
    {
        ref FlightControl control = ref _player.Get<FlightControl>();

        // Losing focus releases the controls rather than freezing them, so a key held as the
        // player alt-tabs away does not leave the ship thrusting or spinning indefinitely.
        if (!IsActive || ImGuiRenderer.WantsKeyboard)
        {
            control = default;
            return;
        }

        KeyboardState keys = Keyboard.GetState();

        control.Thrust = new SimVector3(
            Axis(keys, Keys.D, Keys.A),
            Axis(keys, Keys.R, Keys.F),
            Axis(keys, Keys.W, Keys.S));

        control.Turn = new SimVector3(
            Axis(keys, Keys.Down, Keys.Up),
            Axis(keys, Keys.Right, Keys.Left),
            Axis(keys, Keys.E, Keys.Q));
    }

    private static float Axis(KeyboardState keys, Keys positive, Keys negative) =>
        (keys.IsKeyDown(positive) ? 1f : 0f) - (keys.IsKeyDown(negative) ? 1f : 0f);

    /// <summary>Chase camera: behind and above the player ship, in its own frame.</summary>
    private void UpdateCamera()
    {
        Pose pose = _player.Get<Pose>();

        SimVector3 offset = SimVector3.Transform(new SimVector3(0f, 12f, -60f), pose.Orientation);
        _camera.Position = Camera.ToXna(pose.Position + offset);
        _camera.Orientation = Camera.ToXna(pose.Orientation);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(4, 6, 12));

        Matrix view = _camera.View;
        Matrix projection = _camera.Projection(GraphicsDevice.Viewport.AspectRatio);

        DrawSector(view, projection);
        _plugins.Draw(new RenderView(view, projection));
        _shapes.End(view, projection);

        _imgui.BeginLayout(gameTime);
        _overlay.Draw(_context, _plugins, _clock);
        _imgui.EndLayout();

        base.Draw(gameTime);
    }

    /// <summary>
    /// Draws each ship as its imported mesh, falling back to a debug box while the mesh is still
    /// loading or when there is no asset cache at all.
    /// </summary>
    private void DrawSector(Matrix view, Matrix projection)
    {
        _shapes.Begin();
        DrawReferenceGrid();

        _meshes.Begin(view, projection);

        ReadOnlySpan<Entity> entities = _renderable.GetEntities();
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            Pose pose = entity.Get<Pose>();
            Collider collider = entity.Get<Collider>();

            Vector3 position = Camera.ToXna(pose.Position);
            Quaternion orientation = Camera.ToXna(pose.Orientation);
            bool isPlayer = entity.Has<PlayerControlled>();

            GpuMesh? mesh = _assets.Request(_universe.Ships[entity.Get<ShipRef>().DefinitionIndex].XbtfBodyId);

            if (mesh is not null)
            {
                _meshes.Draw(mesh, Matrix.CreateFromQuaternion(orientation) * Matrix.CreateTranslation(position));
                continue;
            }

            _shapes.Box(position, orientation, collider.Radius, isPlayer ? Color.White : Color.SlateGray);
            _shapes.Axes(position, orientation, collider.Radius * 2.5f);
        }
    }

    /// <summary>A plane of lines so motion is legible without any art in the scene.</summary>
    private void DrawReferenceGrid()
    {
        const int lines = 40;
        const float spacing = 250f;
        const float extent = lines * spacing * 0.5f;
        Color color = new(20, 40, 60);

        for (int i = 0; i <= lines; i++)
        {
            float offset = -extent + i * spacing;
            _shapes.Line(new Vector3(offset, -200f, -extent), new Vector3(offset, -200f, extent), color);
            _shapes.Line(new Vector3(-extent, -200f, offset), new Vector3(extent, -200f, offset), color);
        }
    }

    protected override void UnloadContent()
    {
        _imgui.Dispose();
        _assets.Dispose();
        _meshes.Dispose();
        _shapes.Dispose();
        _renderable.Dispose();
        _universe.Dispose();
    }
}
