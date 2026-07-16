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
    private int _width = 1;
    private int _height = 1;

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
    public void DrawPanel()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        // NoScrollWithMouse: bez toho ti kolecko (zoom kamery) posouva ImGui okno.
        ImGui.Begin("Viewport",
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse);

        if (DrawToolbar is not null)
        {
            // Toolbar chce normalni odsazeni, okno ma padding 0.
            ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(6, 4));
            DrawToolbar();
            ImGui.Spacing();
        }

        Vector2 avail = ImGui.GetContentRegionAvail();
        int w = Math.Max(1, (int)avail.X);
        int h = Math.Max(1, (int)avail.Y);

        // Realokace RenderTexture je drahá (FBO na GPU) - jen pri skutecne zmene rozmeru.
        if (w != _width || h != _height)
        {
            Raylib.UnloadRenderTexture(_rt);
            _rt = Raylib.LoadRenderTexture(w, h);
            _width = w;
            _height = h;
        }

        // ImageRenderTexture resi Y-flip (OpenGL FBO ma obracenou osu Y).
        rlImGui.ImageRenderTexture(_rt);

        Hovered = ImGui.IsItemHovered();
        Origin = ImGui.GetItemRectMin();

        ImGui.End();
        ImGui.PopStyleVar();

        UpdateCaptureState();
    }

    private void UpdateCaptureState()
    {
        var io = ImGui.GetIO();

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

    /// <summary>Klavesove zkratky enginu smi bezet jen kdyz ImGui nezere klavesnici (psani do inspektoru).</summary>
    public static bool GameKeyboardAllowed => !ImGui.GetIO().WantCaptureKeyboard;

    /// <summary>Paprsek z mysi do sceny. Souradnice MUSI byt relativni k viewportu, ne k obrazovce.</summary>
    public Ray GetPickRay(Camera3D camera)
    {
        Vector2 local = Raylib.GetMousePosition() - Origin;
        // Pozn.: v raylib 5.5+ je to GetScreenToWorldRayEx (drive GetMouseRay).
        return Raylib.GetScreenToWorldRayEx(local, camera, _width, _height);
    }

    public void Dispose() => Raylib.UnloadRenderTexture(_rt);
}
