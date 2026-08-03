using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace OpenXt.Game.Rendering;

/// <summary>
/// MonoGame backend for Dear ImGui.
///
/// We own this because there is no maintained ImGui-for-MonoGame package: the only one on NuGet
/// (MonoGame.ImGuiNet) ships its assembly outside lib/, so NuGet will not reference it. It is
/// written against the ImGui.NET version pinned in Directory.Packages.props — 1.92 replaced the
/// font atlas texture API, so bumping that pin means editing this file.
///
/// Debug tooling only. The shipping HUD is not ImGui.
/// </summary>
public sealed class ImGuiRenderer : IDisposable
{
    private static readonly VertexDeclaration DrawVertDeclaration = new(
        Marshal.SizeOf<ImDrawVert>(),
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0));

    private readonly GraphicsDevice _device;
    private readonly Microsoft.Xna.Framework.Game _game;
    private readonly Dictionary<IntPtr, Texture2D> _textures = [];
    private readonly RasterizerState _rasterizer = new()
    {
        CullMode = CullMode.None,
        DepthBias = 0,
        FillMode = FillMode.Solid,
        MultiSampleAntiAlias = false,
        ScissorTestEnable = true,
        SlopeScaleDepthBias = 0,
    };

    private BasicEffect? _effect;
    private VertexBuffer? _vertexBuffer;
    private IndexBuffer? _indexBuffer;
    private byte[] _vertexData = [];
    private byte[] _indexData = [];
    private int _vertexBufferSize;
    private int _indexBufferSize;

    private IntPtr _fontTextureId;
    private int _nextTextureId = 1;
    private int _scrollWheelValue;
    private int _horizontalScrollWheelValue;

    public ImGuiRenderer(Microsoft.Xna.Framework.Game game)
    {
        _device = game.GraphicsDevice;
        _game = game;

        ImGui.SetCurrentContext(ImGui.CreateContext());
        ImGui.StyleColorsDark();

        ImGuiIOPtr io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.Fonts.AddFontDefault();

        game.Window.TextInput += OnTextInput;

        RebuildFontAtlas();
    }

    /// <summary>Uploads the ImGui font atlas as a texture. Call again after adding fonts.</summary>
    public unsafe void RebuildFontAtlas()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixelData, out int width, out int height, out int bytesPerPixel);

        byte[] pixels = new byte[width * height * bytesPerPixel];
        Marshal.Copy(new IntPtr(pixelData), pixels, 0, pixels.Length);

        Texture2D texture = new(_device, width, height, false, SurfaceFormat.Color);
        texture.SetData(pixels);

        if (_fontTextureId != IntPtr.Zero)
            UnbindTexture(_fontTextureId);

        _fontTextureId = BindTexture(texture);
        io.Fonts.SetTexID(_fontTextureId);
        io.Fonts.ClearTexData();
    }

    /// <summary>Registers a texture so ImGui can draw it, returning the handle to pass to ImGui.Image.</summary>
    public IntPtr BindTexture(Texture2D texture)
    {
        IntPtr id = new(_nextTextureId++);
        _textures.Add(id, texture);
        return id;
    }

    public void UnbindTexture(IntPtr textureId) => _textures.Remove(textureId);

    /// <summary>Starts an ImGui frame. Issue ImGui calls between this and <see cref="EndLayout"/>.</summary>
    public void BeginLayout(GameTime gameTime)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.DeltaTime = Math.Max((float)gameTime.ElapsedGameTime.TotalSeconds, 1f / 1000f);
        io.DisplaySize = new System.Numerics.Vector2(
            _device.PresentationParameters.BackBufferWidth,
            _device.PresentationParameters.BackBufferHeight);
        io.DisplayFramebufferScale = new System.Numerics.Vector2(1f, 1f);

        UpdateInput(io);
        ImGui.NewFrame();
    }

    public void EndLayout()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    /// <summary>True when ImGui wants the mouse/keyboard, so game input should ignore it this frame.</summary>
    public static bool WantsMouse => ImGui.GetIO().WantCaptureMouse;

    public static bool WantsKeyboard => ImGui.GetIO().WantCaptureKeyboard;

    private void OnTextInput(object? sender, TextInputEventArgs args)
    {
        if (args.Character == '\t')
            return;

        ImGui.GetIO().AddInputCharacter(args.Character);
    }

    /// <summary>
    /// Feeds ImGui the current input state.
    ///
    /// <see cref="Keyboard"/> and <see cref="Mouse"/> report the desktop's global state, not this
    /// window's, so an unfocused game would otherwise react to whatever the user is typing
    /// elsewhere. When the window is not focused we report everything released rather than simply
    /// skipping the update — skipping would leave any key held at the moment focus was lost stuck
    /// down inside ImGui until it happened to be pressed again.
    /// </summary>
    private void UpdateInput(ImGuiIOPtr io)
    {
        if (!_game.IsActive)
        {
            ReleaseAllInput(io);
            return;
        }

        MouseState mouse = Mouse.GetState();
        KeyboardState keyboard = Keyboard.GetState();

        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(3, mouse.XButton1 == ButtonState.Pressed);
        io.AddMouseButtonEvent(4, mouse.XButton2 == ButtonState.Pressed);

        io.AddMouseWheelEvent(
            (mouse.HorizontalScrollWheelValue - _horizontalScrollWheelValue) / 120f,
            (mouse.ScrollWheelValue - _scrollWheelValue) / 120f);
        _scrollWheelValue = mouse.ScrollWheelValue;
        _horizontalScrollWheelValue = mouse.HorizontalScrollWheelValue;

        foreach ((Keys key, ImGuiKey imKey) in KeyMap)
            io.AddKeyEvent(imKey, keyboard.IsKeyDown(key));

        io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
        io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
        io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt));
        io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows));
    }

    private void ReleaseAllInput(ImGuiIOPtr io)
    {
        // ImGui's convention for "the cursor is not over this window at all".
        io.AddMousePosEvent(float.MinValue, float.MinValue);

        for (int button = 0; button < 5; button++)
            io.AddMouseButtonEvent(button, false);

        foreach ((Keys _, ImGuiKey imKey) in KeyMap)
            io.AddKeyEvent(imKey, false);

        io.AddKeyEvent(ImGuiKey.ModCtrl, false);
        io.AddKeyEvent(ImGuiKey.ModShift, false);
        io.AddKeyEvent(ImGuiKey.ModAlt, false);
        io.AddKeyEvent(ImGuiKey.ModSuper, false);

        // Keep the wheel baseline current so refocusing does not deliver one huge scroll delta.
        MouseState mouse = Mouse.GetState();
        _scrollWheelValue = mouse.ScrollWheelValue;
        _horizontalScrollWheelValue = mouse.HorizontalScrollWheelValue;
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0)
            return;

        Viewport lastViewport = _device.Viewport;
        Rectangle lastScissor = _device.ScissorRectangle;
        RasterizerState lastRasterizer = _device.RasterizerState;
        DepthStencilState lastDepthStencil = _device.DepthStencilState;
        BlendState lastBlend = _device.BlendState;

        _device.BlendFactor = Color.White;
        _device.BlendState = BlendState.NonPremultiplied;
        _device.RasterizerState = _rasterizer;
        _device.DepthStencilState = DepthStencilState.None;
        _device.Viewport = new Viewport(0, 0, _device.PresentationParameters.BackBufferWidth,
            _device.PresentationParameters.BackBufferHeight);

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        UpdateBuffers(drawData);
        RenderCommandLists(drawData);

        _device.Viewport = lastViewport;
        _device.ScissorRectangle = lastScissor;
        _device.RasterizerState = lastRasterizer;
        _device.DepthStencilState = lastDepthStencil;
        _device.BlendState = lastBlend;
    }

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0)
            return;

        if (drawData.TotalVtxCount > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = (int)(drawData.TotalVtxCount * 1.5f);
            _vertexBuffer = new VertexBuffer(_device, DrawVertDeclaration, _vertexBufferSize, BufferUsage.None);
            _vertexData = new byte[_vertexBufferSize * DrawVertDeclaration.VertexStride];
        }

        if (drawData.TotalIdxCount > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = (int)(drawData.TotalIdxCount * 1.5f);
            _indexBuffer = new IndexBuffer(_device, IndexElementSize.SixteenBits, _indexBufferSize, BufferUsage.None);
            _indexData = new byte[_indexBufferSize * sizeof(ushort)];
        }

        int vertexOffset = 0;
        int indexOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            fixed (void* vertexDestination = &_vertexData[vertexOffset * DrawVertDeclaration.VertexStride])
            {
                Buffer.MemoryCopy(
                    cmdList.VtxBuffer.Data.ToPointer(), vertexDestination,
                    _vertexData.Length - vertexOffset * DrawVertDeclaration.VertexStride,
                    cmdList.VtxBuffer.Size * DrawVertDeclaration.VertexStride);
            }

            fixed (void* indexDestination = &_indexData[indexOffset * sizeof(ushort)])
            {
                Buffer.MemoryCopy(
                    cmdList.IdxBuffer.Data.ToPointer(), indexDestination,
                    _indexData.Length - indexOffset * sizeof(ushort),
                    cmdList.IdxBuffer.Size * sizeof(ushort));
            }

            vertexOffset += cmdList.VtxBuffer.Size;
            indexOffset += cmdList.IdxBuffer.Size;
        }

        _vertexBuffer!.SetData(_vertexData, 0, drawData.TotalVtxCount * DrawVertDeclaration.VertexStride);
        _indexBuffer!.SetData(_indexData, 0, drawData.TotalIdxCount * sizeof(ushort));
    }

    private void RenderCommandLists(ImDrawDataPtr drawData)
    {
        if (_vertexBuffer is null || _indexBuffer is null)
            return;

        _device.SetVertexBuffer(_vertexBuffer);
        _device.Indices = _indexBuffer;

        int vertexOffset = 0;
        int indexOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                ImDrawCmdPtr command = cmdList.CmdBuffer[i];
                if (command.ElemCount == 0)
                    continue;

                if (!_textures.TryGetValue(command.TextureId, out Texture2D? texture))
                    throw new InvalidOperationException(
                        $"ImGui asked for texture {command.TextureId}, which was never bound.");

                _device.ScissorRectangle = new Rectangle(
                    (int)command.ClipRect.X,
                    (int)command.ClipRect.Y,
                    (int)(command.ClipRect.Z - command.ClipRect.X),
                    (int)(command.ClipRect.W - command.ClipRect.Y));

                BasicEffect effect = UpdateEffect(texture);
                foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _device.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        (int)command.VtxOffset + vertexOffset,
                        (int)command.IdxOffset + indexOffset,
                        (int)command.ElemCount / 3);
                }
            }

            vertexOffset += cmdList.VtxBuffer.Size;
            indexOffset += cmdList.IdxBuffer.Size;
        }
    }

    private BasicEffect UpdateEffect(Texture2D texture)
    {
        _effect ??= new BasicEffect(_device);

        ImGuiIOPtr io = ImGui.GetIO();
        _effect.World = Matrix.Identity;
        _effect.View = Matrix.Identity;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0f, io.DisplaySize.X, io.DisplaySize.Y, 0f, -1f, 1f);
        _effect.TextureEnabled = true;
        _effect.Texture = texture;
        _effect.VertexColorEnabled = true;

        return _effect;
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _rasterizer.Dispose();

        foreach (Texture2D texture in _textures.Values)
            texture.Dispose();
        _textures.Clear();
    }

    /// <summary>Enough of the keyboard for debug tooling; extend when a tool needs more.</summary>
    private static readonly (Keys, ImGuiKey)[] KeyMap =
    [
        (Keys.Tab, ImGuiKey.Tab),
        (Keys.Left, ImGuiKey.LeftArrow),
        (Keys.Right, ImGuiKey.RightArrow),
        (Keys.Up, ImGuiKey.UpArrow),
        (Keys.Down, ImGuiKey.DownArrow),
        (Keys.PageUp, ImGuiKey.PageUp),
        (Keys.PageDown, ImGuiKey.PageDown),
        (Keys.Home, ImGuiKey.Home),
        (Keys.End, ImGuiKey.End),
        (Keys.Insert, ImGuiKey.Insert),
        (Keys.Delete, ImGuiKey.Delete),
        (Keys.Back, ImGuiKey.Backspace),
        (Keys.Space, ImGuiKey.Space),
        (Keys.Enter, ImGuiKey.Enter),
        (Keys.Escape, ImGuiKey.Escape),
        (Keys.A, ImGuiKey.A),
        (Keys.C, ImGuiKey.C),
        (Keys.V, ImGuiKey.V),
        (Keys.X, ImGuiKey.X),
        (Keys.Y, ImGuiKey.Y),
        (Keys.Z, ImGuiKey.Z),
    ];
}
