using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenXt.Game.Debug;
using OpenXt.Game.Rendering;
using OpenXt.Sim;
using OpenXt.Sim.Components;
using OpenXt.Sim.Data;

// Inside this project, unqualified Vector3/Quaternion/Matrix are MonoGame's. Simulation vectors are
// System.Numerics and stay explicitly named — the conversion happens only at the render boundary.
using SimVector3 = System.Numerics.Vector3;
using SimQuaternion = System.Numerics.Quaternion;

namespace OpenXt.Game;

/// <summary>
/// The MonoGame entry point. It owns the window, the graphics device and the frame loop, and
/// nothing else — all world state lives in <see cref="Universe"/>, which knows nothing about it.
/// </summary>
public sealed class OpenXtGame : Microsoft.Xna.Framework.Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly FixedStepClock _clock = new(stepSeconds: 1f / 60f);

    private Universe _universe = null!;
    private Sector _sector = null!;
    private EntitySet _renderable = null!;
    private Entity _player;

    private DebugShapeRenderer _shapes = null!;
    private ImGuiRenderer _imgui = null!;
    private DebugOverlay _overlay = null!;
    private Camera _camera = new();

    public OpenXtGame()
    {
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
        Window.Title = "OpenXT";
    }

    protected override void Initialize()
    {
        string catalogPath = Path.Combine(AppContext.BaseDirectory, "data", "ships", "ships.json");
        ShipCatalog catalog = ShipCatalog.LoadFile(catalogPath);

        _universe = new Universe(catalog);
        _sector = _universe.CreateSector("Argon Prime");

        _player = _sector.SpawnShip("argon_discoverer", SimVector3.Zero, SimQuaternion.Identity);
        _player.Set<PlayerControlled>();

        // Placeholder traffic, so there is something to look at and something for the broadphase to chew on.
        for (int i = 1; i <= 8; i++)
        {
            _sector.SpawnShip(
                i % 2 == 0 ? "argon_mercury" : "teladi_vulture",
                new SimVector3(i * 140f - 600f, MathF.Sin(i) * 90f, 400f + i * 60f),
                SimQuaternion.CreateFromYawPitchRoll(i * 0.4f, 0f, 0f));
        }

        // Built once. Rebuilding an EntitySet per frame would allocate and leak subscriptions.
        _renderable = _sector.World.GetEntities().With<Pose>().With<Collider>().AsSet();

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _shapes = new DebugShapeRenderer(GraphicsDevice);
        _imgui = new ImGuiRenderer(this);
        _overlay = new DebugOverlay();
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        ReadFlightInput();

        int steps = _clock.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);
        for (int i = 0; i < steps; i++)
            _universe.Step(_clock.Step);

        UpdateCamera();

        base.Update(gameTime);
    }

    /// <summary>
    /// Translates the keyboard into normalised flight intent. Input never touches
    /// <see cref="Motion"/> directly — the flight model is the only thing that moves a ship.
    /// </summary>
    private void ReadFlightInput()
    {
        ref FlightControl control = ref _player.Get<FlightControl>();

        if (ImGuiRenderer.WantsKeyboard)
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

        DrawSector();
        _shapes.End(view, projection);

        _imgui.BeginLayout(gameTime);
        _overlay.Draw(_universe, _clock, _player);
        _imgui.EndLayout();

        base.Draw(gameTime);
    }

    private void DrawSector()
    {
        _shapes.Begin();
        DrawReferenceGrid();

        ReadOnlySpan<Entity> entities = _renderable.GetEntities();
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            Pose pose = entity.Get<Pose>();
            Collider collider = entity.Get<Collider>();

            Vector3 position = Camera.ToXna(pose.Position);
            Quaternion orientation = Camera.ToXna(pose.Orientation);
            bool isPlayer = entity.Has<PlayerControlled>();

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
        _shapes.Dispose();
        _renderable.Dispose();
        _universe.Dispose();
    }
}
