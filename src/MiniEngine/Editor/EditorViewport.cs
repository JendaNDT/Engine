using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace MiniEngine.Editor;

/// <summary>
/// 3D viewport jako ImGui okno.
/// Scena se renderuje do RenderTexture2D PRED BeginDrawing(), ImGui pak kresli jen obrazek.
/// </summary>
public sealed class EditorViewport : IDisposable
{
    private RenderTexture2D _rt;
    private RenderTexture2D _postProcessedRt;
    private int _width = 1;
    private int _height = 1;

    public Rendering.PostProcessing PostProcessor;

    public string? DroppedModelPath { get; set; }
    public Vector3 DroppedPosition { get; set; }
    public string? DroppedTexturePath { get; set; }
    public string? DroppedPrefabPath { get; set; }
    public Vector3 DroppedPrefabPosition { get; set; }

    /// <summary>true = uzivatel drzi pravou mys nad viewportem, kamera ma vyhradni kontrolu.</summary>
    public bool Captured { get; private set; }
    public bool Hovered { get; private set; }

    /// <summary>Levy horni roh obrazku na obrazovce - nutny pro prepocet mysi pri pickingu.</summary>
    public Vector2 Origin { get; private set; }

    public int Width => _width;
    public int Height => _height;

    /// <summary>Volitelny toolbar kresleny nad obrazem sceny (tlacitka kamery, napoveda).</summary>
    public Action? DrawToolbar;

    public EditorViewport()
    {
        _width = 1280;
        _height = 720;
        _rt = Raylib.LoadRenderTexture(_width, _height);
        _postProcessedRt = Raylib.LoadRenderTexture(_width, _height);
        PostProcessor = new Rendering.PostProcessing();
    }

    /// <summary>Krok 1: vykresli scenu do offscreen textury. Vola se PRED BeginDrawing().</summary>
    public void RenderScene(Camera3D camera, Action<Camera3D> drawScene)
    {
        Raylib.BeginTextureMode(_rt);
        Raylib.ClearBackground(new Color(30, 32, 36, 255));
        Raylib.BeginMode3D(camera);
        drawScene(camera);
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
    }

    /// <summary>Krok 2: ImGui okno s obrazkem. Vola se mezi rlImGui.Begin() a rlImGui.End().</summary>
    public unsafe void DrawPanel(Camera3D camera)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        // NoScrollWithMouse: bez toho ti kolecko (zoom kamery) posouva ImGui okno.
        ImGui.Begin("Viewport",
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse);

        Vector2 avail = ImGui.GetContentRegionAvail();
        int w = Math.Max(1, (int)avail.X);
        int h = Math.Max(1, (int)avail.Y);

        // Realokace RenderTexture je drahá (FBO na GPU) - jen pri skutecne zmene rozmeru.
        if (w != _width || h != _height)
        {
            Raylib.UnloadRenderTexture(_rt);
            Raylib.UnloadRenderTexture(_postProcessedRt);
            _rt = Raylib.LoadRenderTexture(w, h);
            _postProcessedRt = Raylib.LoadRenderTexture(w, h);
            _width = w;
            _height = h;
        }

        Vector2 screenPos = ImGui.GetCursorScreenPos();

        if (PostProcessor.Enabled)
        {
            PostProcessor.Apply();
            Raylib.BeginTextureMode(_postProcessedRt);
            Raylib.BeginShaderMode(PostProcessor.Shader);
            
            Rectangle sourceRec = new Rectangle(0, 0, _rt.Texture.Width, -_rt.Texture.Height);
            Rectangle destRec = new Rectangle(0, 0, _postProcessedRt.Texture.Width, _postProcessedRt.Texture.Height);
            Raylib.DrawTexturePro(_rt.Texture, sourceRec, destRec, Vector2.Zero, 0f, Color.White);
            
            Raylib.EndShaderMode();
            Raylib.EndTextureMode();
            
            rlImGui.ImageRenderTexture(_postProcessedRt);
        }
        else
        {
            rlImGui.ImageRenderTexture(_rt);
        }

        // Draw HUD overlay on top of the image
        if (DrawToolbar is not null)
        {
            ImGui.SetNextWindowPos(screenPos + new Vector2(10, 10));
            ImGui.SetNextWindowSize(new Vector2(avail.X - 20, 36f));
            ImGui.SetNextWindowBgAlpha(0.75f);
            
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 5));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            
            if (ImGui.Begin("ViewportHUD", ImGuiWindowFlags.NoTitleBar | 
                                           ImGuiWindowFlags.NoResize | 
                                           ImGuiWindowFlags.NoMove | 
                                           ImGuiWindowFlags.NoScrollbar | 
                                           ImGuiWindowFlags.NoSavedSettings | 
                                           ImGuiWindowFlags.NoFocusOnAppearing | 
                                           ImGuiWindowFlags.NoDocking))
            {
                DrawToolbar();
                ImGui.End();
            }
            
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor();
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_MODEL_PATH");
            if (payload.NativePtr != null)
            {
                unsafe
                {
                    int length = payload.DataSize;
                    byte* dataPtr = (byte*)payload.Data;
                    byte[] bytes = new byte[length];
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)dataPtr, bytes, 0, length);
                    string relPath = System.Text.Encoding.UTF8.GetString(bytes);

                    DroppedModelPath = relPath;
                    
                    Ray ray = GetPickRay(camera);
                    if (MathF.Abs(ray.Direction.Y) > 0.0001f)
                    {
                        float t = -ray.Position.Y / ray.Direction.Y;
                        if (t > 0f) DroppedPosition = ray.Position + ray.Direction * t;
                        else DroppedPosition = ray.Position + ray.Direction * 5f;
                    }
                    else
                    {
                        DroppedPosition = ray.Position + ray.Direction * 5f;
                    }
                }
            }

            var texPayload = ImGui.AcceptDragDropPayload("ASSET_TEXTURE_PATH");
            unsafe
            {
                if (texPayload.NativePtr != null)
                {
                    int length = texPayload.DataSize;
                    byte* dataPtr = (byte*)texPayload.Data;
                    byte[] bytes = new byte[length];
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)dataPtr, bytes, 0, length);
                    string relPath = System.Text.Encoding.UTF8.GetString(bytes);

                    DroppedTexturePath = relPath;
                }
            }

            var prefabPayload = ImGui.AcceptDragDropPayload("ASSET_PREFAB_PATH");
            unsafe
            {
                if (prefabPayload.NativePtr != null)
                {
                    int length = prefabPayload.DataSize;
                    byte* dataPtr = (byte*)prefabPayload.Data;
                    byte[] bytes = new byte[length];
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)dataPtr, bytes, 0, length);
                    string relPath = System.Text.Encoding.UTF8.GetString(bytes);

                    DroppedPrefabPath = relPath;

                    Ray ray = GetPickRay(camera);
                    if (MathF.Abs(ray.Direction.Y) > 0.0001f)
                    {
                        float t = -ray.Position.Y / ray.Direction.Y;
                        if (t > 0f) DroppedPrefabPosition = ray.Position + ray.Direction * t;
                        else DroppedPrefabPosition = ray.Position + ray.Direction * 5f;
                    }
                    else
                    {
                        DroppedPrefabPosition = ray.Position + ray.Direction * 5f;
                    }
                }
            }

            ImGui.EndDragDropTarget();
        }

        Hovered = ImGui.IsItemHovered();
        Origin = ImGui.GetItemRectMin();

        DrawHelpOverlay();

        ImGui.End();
        ImGui.PopStyleVar();

        UpdateCaptureState();
    }

    private void UpdateCaptureState()
    {
        var io = ImGui.GetIO();

        // Pokud okno ztratilo fokus, nuceně uvolníme kurzor a zrušíme zachycení.
        if (Captured && !Raylib.IsWindowFocused())
        {
            Captured = false;
            Raylib.EnableCursor();
        }

        if (!Captured && Hovered && Raylib.IsMouseButtonPressed(MouseButton.Right))
        {
            Captured = true;
            Raylib.DisableCursor();          // relativni mod
        }
        else if (Captured && Raylib.IsMouseButtonReleased(MouseButton.Right))
        {
            Captured = false;
            Raylib.EnableCursor();
        }

        // Dokud drzime kameru, ImGui mys vubec nevidi - jinak by se panely
        // pod prejizdejicim kurzorem rozsvecovaly a reagovaly.
        if (Captured) io.ConfigFlags |= ImGuiConfigFlags.NoMouse;
        else io.ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
    }

    private bool _showHelp = true;

    private void DrawHelpOverlay()
    {
        Vector2 padding = new Vector2(10, 10);
        Vector2 windowPos = Origin + new Vector2(_width - padding.X, padding.Y);
        
        ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always, new Vector2(1.0f, 0.0f));
        ImGui.SetNextWindowBgAlpha(0.75f);
        
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar |
                                 ImGuiWindowFlags.NoResize |
                                 ImGuiWindowFlags.AlwaysAutoResize |
                                 ImGuiWindowFlags.NoMove |
                                 ImGuiWindowFlags.NoSavedSettings |
                                 ImGuiWindowFlags.NoFocusOnAppearing |
                                 ImGuiWindowFlags.NoNav;

        if (ImGui.Begin("ViewportHelpOverlay", flags))
        {
            if (_showHelp)
            {
                ImGui.TextColored(new Vector4(0.24f, 0.55f, 0.95f, 1f), "Ovládání & Klávesové zkratky");
                ImGui.Separator();
                
                ImGui.Text("Pravá myš + WASD: Let kamerou (Shift = turbo)");
                ImGui.Text("Alt + Levá myš:   Rozhlížení");
                ImGui.Text("Kolečko myši:     Přiblížení / Zoom");
                ImGui.Text("W / E / R:        Režim Gizma (Posun/Rotace/Měřítko)");
                ImGui.Text("F:                Focus kamery na vybraný objekt");
                ImGui.Text("Cmd+D / Ctrl+D:   Duplikovat vybraný objekt");
                ImGui.Text("Delete:           Smazat vybraný objekt");
                
                ImGui.Separator();
                if (ImGui.Button("Skrýt nápovědu"))
                {
                    _showHelp = false;
                }
            }
            else
            {
                if (ImGui.Button("?"))
                {
                    _showHelp = true;
                }
            }
            ImGui.End();
        }
    }

    /// <summary>Klavesove zkratky enginu smi bezet jen kdyz ImGui nezere klavesnici (psani do inspektoru).</summary>
    public static bool GameKeyboardAllowed => !ImGui.GetIO().WantCaptureKeyboard;

    /// <summary>Paprsek z mysi do sceny. Souradnice MUSI byt relativni k viewportu, ne k obrazovce.</summary>
    public Ray GetPickRay(Camera3D camera)
    {
        Vector2 local = Raylib.GetMousePosition() - Origin;
        // Pozn.: v raylib 5.5+ je to GetScreenToWorldRayEx (drive GetMouseRay).
        return Raylib.GetScreenToWorldRayEx(local, camera, _width, _height);
    }

    public void Dispose()
    {
        Raylib.UnloadRenderTexture(_rt);
        Raylib.UnloadRenderTexture(_postProcessedRt);
        PostProcessor.Dispose();
    }
}
