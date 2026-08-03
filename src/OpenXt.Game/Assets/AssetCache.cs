using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenXt.Assets;

namespace OpenXt.Game.Assets;

/// <summary>
/// Loads converted assets from the local cache produced by <c>openxt-import import</c>.
///
/// Two rules shape the design:
///
/// * <b>Nothing blocks the frame loop.</b> Disk reads and mesh parsing happen on the thread pool.
///   Only the GPU upload runs on the main thread, because creating GL resources off-thread is not
///   safe under DesktopGL — so completed loads queue up and are drained a few per frame.
/// * <b>The game never imports anything itself.</b> If the cache is missing or stale it says so and
///   carries on with debug shapes. Reaching into the player's installation is the importer's job,
///   run deliberately, not something the runtime does behind their back.
/// </summary>
public sealed class AssetCache : IDisposable
{
    /// <summary>GPU uploads per frame. Enough to fill a sector quickly without visible hitching.</summary>
    private const int UploadsPerFrame = 4;

    private readonly GraphicsDevice _device;
    private readonly string _gameKey;

    private readonly ConcurrentQueue<(int BodyId, OxMesh Mesh)> _loaded = new();
    private readonly Dictionary<int, GpuMesh?> _meshes = [];
    private readonly HashSet<int> _requested = [];
    private readonly Dictionary<int, Texture2D?> _textures = [];

    private Texture2D? _fallbackTexture;
    private Dictionary<int, string> _text = [];

    public CacheManifest? Manifest { get; private set; }

    /// <summary>Null when the cache is usable; otherwise why it is not.</summary>
    public string? Problem { get; private set; }

    public bool IsUsable => Problem is null && Manifest is not null;

    public AssetCache(GraphicsDevice device, string gameKey)
    {
        _device = device;
        _gameKey = gameKey;
        Open();
    }

    private void Open()
    {
        string manifestPath = AssetCachePaths.Manifest(_gameKey);

        if (!File.Exists(manifestPath))
        {
            Problem = $"No asset cache at {AssetCachePaths.ForGame(_gameKey)}. " +
                      "Run: dotnet run --project tools/OpenXt.AssetImport -- import";
            return;
        }

        try
        {
            Manifest = CacheManifest.ReadFile(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Problem = $"Asset cache manifest is unreadable: {ex.Message}";
            return;
        }

        if (Manifest is null)
        {
            Problem = "Asset cache manifest is empty.";
            return;
        }

        if (!Manifest.IsCurrent)
        {
            Problem = $"Asset cache was built by importer v{Manifest.ImporterVersion} " +
                      $"(mesh format v{Manifest.MeshVersion}); this build expects " +
                      $"v{CacheManifest.CurrentImporterVersion}/v{OxMesh.CurrentVersion}. " +
                      "Re-run: openxt-import import --force";
            return;
        }

        LoadTextTable();
    }

    private void LoadTextTable()
    {
        // English first, then German, then whatever else was imported — the game has no language
        // setting yet, so this is a stand-in rather than a policy.
        foreach (int language in (int[])[44, 49])
        {
            string path = AssetCachePaths.TextFile(_gameKey, language);
            if (!File.Exists(path))
                continue;

            try
            {
                using FileStream stream = File.OpenRead(path);
                _text = JsonSerializer.Deserialize(stream, AssetJsonContext.Default.DictionaryInt32String)
                        ?? [];
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // A missing or corrupt text table costs names, not the run.
                _text = [];
            }
        }
    }

    /// <summary>The archive's string for an id, or null. Ship names live at 3000-3999.</summary>
    public string? Text(int id) => id >= 0 && _text.TryGetValue(id, out string? value) ? value : null;

    /// <summary>
    /// Asks for a mesh. Returns immediately; the first call starts a background load and later
    /// calls return the mesh once <see cref="Update"/> has uploaded it.
    /// </summary>
    public GpuMesh? Request(int bodyId)
    {
        if (bodyId < 0 || !IsUsable)
            return null;

        if (_meshes.TryGetValue(bodyId, out GpuMesh? mesh))
            return mesh;

        if (!_requested.Add(bodyId))
            return null;

        string path = AssetCachePaths.MeshFile(_gameKey, bodyId);

        Task.Run(() =>
        {
            try
            {
                _loaded.Enqueue((bodyId, OxMesh.ReadFile(path)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                // Record the miss so we neither retry forever nor pretend it is still loading.
                _loaded.Enqueue((bodyId, null!));
            }
        });

        return null;
    }

    /// <summary>Drains finished loads onto the GPU. Main thread only; call once per frame.</summary>
    public void Update()
    {
        for (int i = 0; i < UploadsPerFrame && _loaded.TryDequeue(out (int BodyId, OxMesh Mesh) item); i++)
        {
            if (item.Mesh is null)
            {
                _meshes[item.BodyId] = null;
                continue;
            }

            try
            {
                _meshes[item.BodyId] = Upload(item.Mesh);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                _meshes[item.BodyId] = null;
                Console.Error.WriteLine($"body {item.BodyId}: upload failed, {ex.Message}");
            }
        }
    }

    private GpuMesh? Upload(OxMesh mesh)
    {
        if (mesh.Lods.Length == 0)
            return null;

        // Only LOD 0 for now; nothing selects levels of detail yet.
        OxLod lod = mesh.Lods[0];
        if (lod.Vertices.Length == 0 || lod.Submeshes.Length == 0)
            return null;

        VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[lod.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            OxVertex source = lod.Vertices[i];
            vertices[i] = new VertexPositionNormalTexture(
                new Vector3(source.Position.X, source.Position.Y, source.Position.Z),
                new Vector3(source.Normal.X, source.Normal.Y, source.Normal.Z),
                new Vector2(source.TexCoord.X, source.TexCoord.Y));
        }

        // One index buffer for the whole mesh; submeshes are ranges within it.
        int total = 0;
        foreach (OxSubmesh submesh in lod.Submeshes)
            total += submesh.Indices.Length;

        int[] indices = new int[total];
        GpuSubmesh[] submeshes = new GpuSubmesh[lod.Submeshes.Length];
        int cursor = 0;

        for (int s = 0; s < lod.Submeshes.Length; s++)
        {
            OxSubmesh source = lod.Submeshes[s];
            Array.Copy(source.Indices, 0, indices, cursor, source.Indices.Length);

            Texture2D? texture = Texture(source.TextureId, out bool textured);

            submeshes[s] = new GpuSubmesh
            {
                StartIndex = cursor,
                PrimitiveCount = source.Indices.Length / 3,
                Texture = texture,
                // The material's diffuse colour stands in for a texture, it does not tint one.
                // Multiplying a real hull texture by it just darkens everything.
                Diffuse = textured
                    ? Vector3.One
                    : new Vector3(source.Diffuse.X, source.Diffuse.Y, source.Diffuse.Z),
            };

            cursor += source.Indices.Length;
        }

        VertexBuffer vertexBuffer = new(
            _device, VertexPositionNormalTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
        vertexBuffer.SetData(vertices);

        IndexBuffer indexBuffer = new(_device, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly);
        indexBuffer.SetData(indices);

        return new GpuMesh
        {
            Vertices = vertexBuffer,
            Indices = indexBuffer,
            Submeshes = submeshes,
            BoundingRadius = mesh.BoundingRadius,
            Title = mesh.Title,
        };
    }

    /// <summary>
    /// Loads a texture, decoding the archive's JPEG bytes directly. Seven texture ids are
    /// referenced by materials but absent from the archive, so a miss is expected and gets the
    /// flat fallback rather than an exception.
    /// </summary>
    private Texture2D? Texture(int textureId, out bool textured)
    {
        textured = false;

        if (textureId < 0)
            return FallbackTexture();

        if (_textures.TryGetValue(textureId, out Texture2D? cached))
        {
            textured = cached is not null;
            return cached ?? FallbackTexture();
        }

        string path = AssetCachePaths.TextureFile(_gameKey, textureId);
        Texture2D? texture = null;

        if (File.Exists(path))
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                texture = Texture2D.FromStream(_device, stream);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                texture = null;
            }
        }

        _textures[textureId] = texture;
        textured = texture is not null;
        return texture ?? FallbackTexture();
    }

    private Texture2D FallbackTexture()
    {
        if (_fallbackTexture is not null)
            return _fallbackTexture;

        _fallbackTexture = new Texture2D(_device, 1, 1);
        _fallbackTexture.SetData([new Color(160, 160, 168)]);
        return _fallbackTexture;
    }

    public void Dispose()
    {
        foreach (GpuMesh? mesh in _meshes.Values)
            mesh?.Dispose();

        foreach (Texture2D? texture in _textures.Values)
            texture?.Dispose();

        _fallbackTexture?.Dispose();
    }
}
