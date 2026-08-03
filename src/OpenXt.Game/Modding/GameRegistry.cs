using OpenXt.Modding;

namespace OpenXt.Game.Modding;

/// <summary>
/// Holds the presentation hooks packages registered, and calls them.
///
/// A hook that throws is disabled after its first failure and reported once. A mod's renderer
/// blowing up should cost the mod its renderer, not turn the game into an exception per frame, and
/// the alternative — letting it propagate — takes the whole window down over someone else's bug.
/// </summary>
public sealed class GameRegistry : IGameRegistry
{
    private sealed class Hook<T>(string id, T value)
    {
        public string Id { get; } = id;
        public T Value { get; } = value;
        public bool Disabled { get; set; }
    }

    private readonly List<Hook<IFrameSystem>> _frameSystems = [];
    private readonly List<Hook<IWorldRenderer>> _renderers = [];
    private readonly List<Hook<IDebugPanel>> _panels = [];
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly ModDiagnostics _diagnostics;

    private GameRegistry(GameContext context)
    {
        Context = context;
        _diagnostics = context.Mods.Diagnostics;
    }

    public GameContext Context { get; }

    public int HookCount => _frameSystems.Count + _renderers.Count + _panels.Count;

    /// <summary>Runs every loaded <see cref="IGamePlugin"/> against a fresh registry.</summary>
    public static GameRegistry Configure(GameContext context)
    {
        GameRegistry registry = new(context);

        foreach (IGamePlugin plugin in context.Mods.PluginsOf<IGamePlugin>())
        {
            (int frame, int render, int panel) checkpoint =
                (registry._frameSystems.Count, registry._renderers.Count, registry._panels.Count);

            try
            {
                plugin.ConfigureGame(registry);
            }
            catch (Exception ex)
            {
                // Third-party code: anything it throws is in scope. Undo its half of the work so a
                // partially configured mod does not draw half a feature.
                registry.RollbackTo(checkpoint);
                context.Mods.Diagnostics.Error(
                    "(plugin)",
                    $"'{plugin.GetType().FullName}' failed while configuring the game layer " +
                    $"and was skipped: {ex.Message}");
            }
        }

        return registry;
    }

    public void AddFrameSystem(string id, IFrameSystem system) => Add(_frameSystems, id, system);

    public void AddWorldRenderer(string id, IWorldRenderer renderer) => Add(_renderers, id, renderer);

    public void AddDebugPanel(string id, IDebugPanel panel) => Add(_panels, id, panel);

    public void Update(float dt)
    {
        for (int i = 0; i < _frameSystems.Count; i++)
            Invoke(_frameSystems[i], static (hook, state) => hook.Update(state.Context, state.Dt), (Context, Dt: dt));
    }

    public void Draw(in RenderView view)
    {
        RenderView local = view;
        for (int i = 0; i < _renderers.Count; i++)
            Invoke(_renderers[i], static (hook, state) => hook.Draw(state.Context, state.View), (Context, View: local));
    }

    /// <summary>The debug panels still enabled, for the overlay to draw.</summary>
    public IEnumerable<(string Id, IDebugPanel Panel)> Panels()
    {
        foreach (Hook<IDebugPanel> hook in _panels)
            if (!hook.Disabled)
                yield return (hook.Id, hook.Value);
    }

    /// <summary>Draws one panel by id, behind the same guard as every other hook.</summary>
    public void DrawPanel(string id)
    {
        foreach (Hook<IDebugPanel> hook in _panels)
        {
            if (!string.Equals(hook.Id, id, StringComparison.Ordinal))
                continue;

            Invoke(hook, static (panel, context) => panel.Draw(context), Context);
            return;
        }
    }

    private void Add<T>(List<Hook<T>> hooks, string id, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(value);

        if (!_ids.Add(id))
            throw new InvalidOperationException($"A game hook with id '{id}' is already registered.");

        hooks.Add(new Hook<T>(id, value));
    }

    private void RollbackTo((int Frame, int Render, int Panel) checkpoint)
    {
        Trim(_frameSystems, checkpoint.Frame);
        Trim(_renderers, checkpoint.Render);
        Trim(_panels, checkpoint.Panel);
    }

    private void Trim<T>(List<Hook<T>> hooks, int count)
    {
        for (int i = hooks.Count - 1; i >= count; i--)
        {
            _ids.Remove(hooks[i].Id);
            hooks.RemoveAt(i);
        }
    }

    private void Invoke<THook, TState>(Hook<THook> hook, Action<THook, TState> call, TState state)
    {
        if (hook.Disabled)
            return;

        try
        {
            call(hook.Value, state);
        }
        catch (Exception ex)
        {
            hook.Disabled = true;
            _diagnostics.Error(hook.Id, $"disabled after it threw: {ex.Message}");
            Console.Error.WriteLine($"mod hook '{hook.Id}' disabled: {ex}");
        }
    }
}
