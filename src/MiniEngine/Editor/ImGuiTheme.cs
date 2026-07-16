using System.Numerics;
using ImGuiNET;

namespace MiniEngine.Editor;

public static class ImGuiTheme
{
    public static void ApplyDarkTheme()
    {
        var style = ImGui.GetStyle();

        // Nastavení zaoblení prvků (Moderní zaoblenější rohy)
        style.WindowRounding = 6.0f;
        style.ChildRounding = 4.0f;
        style.FrameRounding = 4.0f;
        style.PopupRounding = 5.0f;
        style.ScrollbarRounding = 8.0f;
        style.GrabRounding = 4.0f;
        style.TabRounding = 4.0f;

        // Tloušťka okrajů
        style.WindowBorderSize = 1.0f;
        style.FrameBorderSize = 1.0f;
        style.PopupBorderSize = 1.0f;

        // Vlastní barevná paleta (Nordic Charcoal / Gold accents)
        var colors = style.Colors;
        colors[(int)ImGuiCol.Text] = new Vector4(0.90f, 0.90f, 0.93f, 1.00f);
        colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.50f, 0.50f, 0.55f, 1.00f);
        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.08f, 0.08f, 0.10f, 1.00f); // Tmavě šedá
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.10f, 0.10f, 0.12f, 1.00f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.12f, 0.12f, 0.14f, 0.98f);
        colors[(int)ImGuiCol.Border] = new Vector4(0.18f, 0.18f, 0.22f, 1.00f); // Jemný okraj
        colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);

        // Vstupy a rámečky (FrameBg)
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.14f, 0.14f, 0.17f, 1.00f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.20f, 0.20f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.26f, 0.26f, 0.32f, 1.00f);

        // Záhlaví panelů (TitleBg)
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.11f, 0.11f, 0.13f, 1.00f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.15f, 0.15f, 0.18f, 1.00f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.09f, 0.09f, 0.11f, 1.00f);

        // Posuvníky a výběry (Scrollbar, Grab, Checkmark, atd.)
        colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.12f, 0.12f, 0.14f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.10f, 0.10f, 0.12f, 0.50f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.22f, 0.22f, 0.26f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.30f, 0.30f, 0.36f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.38f, 0.38f, 0.44f, 1.00f);
        colors[(int)ImGuiCol.CheckMark] = new Vector4(0.85f, 0.70f, 0.25f, 1.00f); // Warm gold akcent
        colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.85f, 0.70f, 0.25f, 0.85f);
        colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.95f, 0.80f, 0.35f, 1.00f);

        // Tlačítka (Button)
        colors[(int)ImGuiCol.Button] = new Vector4(0.22f, 0.22f, 0.26f, 1.00f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.32f, 0.32f, 0.38f, 1.00f);
        colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.40f, 0.40f, 0.48f, 1.00f);

        // Záhlaví stromů a rozevíračů (Header)
        colors[(int)ImGuiCol.Header] = new Vector4(0.18f, 0.18f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.24f, 0.24f, 0.30f, 1.00f);
        colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.30f, 0.30f, 0.38f, 1.00f);

        // Oddělovače (Separator)
        colors[(int)ImGuiCol.Separator] = new Vector4(0.18f, 0.18f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.30f, 0.30f, 0.38f, 1.00f);
        colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.38f, 0.38f, 0.48f, 1.00f);

        // Záložky (Tab)
        colors[(int)ImGuiCol.Tab] = new Vector4(0.14f, 0.14f, 0.18f, 1.00f);
        colors[(int)ImGuiCol.TabHovered] = new Vector4(0.24f, 0.24f, 0.30f, 1.00f);

        // Dockování a Viewporty (Docking / Nav)
        colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.85f, 0.70f, 0.25f, 0.30f);
        colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(0.08f, 0.08f, 0.10f, 1.00f);
        colors[(int)ImGuiCol.PlotLines] = new Vector4(0.85f, 0.70f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.95f, 0.80f, 0.35f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.85f, 0.70f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.95f, 0.80f, 0.35f, 1.00f);
        colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.85f, 0.70f, 0.25f, 0.35f);
        colors[(int)ImGuiCol.DragDropTarget] = new Vector4(0.85f, 0.70f, 0.25f, 0.90f);
        colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.00f, 1.00f, 1.00f, 0.70f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.20f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.08f, 0.08f, 0.10f, 0.35f);
    }
}
