using System.Numerics;
using ImGuiNET;

namespace MiniEngine.Editor;

/// <summary>
/// Téma "Tmavá evoluce" (varianta 1a) – výukový režim pro žáky ~15 let.
/// Charcoal základ + zlatý akcent zachován, vyšší kontrast textu,
/// větší zaoblení a odsazení pro lepší čitelnost a větší cíle kliknutí.
/// </summary>
public static class ImGuiTheme
{
    public static void ApplyDarkTheme()
    {
        var style = ImGui.GetStyle();

        // Zaoblení – přátelštější, měkčí
        style.WindowRounding = 8.0f;
        style.ChildRounding = 6.0f;
        style.FrameRounding = 6.0f;
        style.PopupRounding = 7.0f;
        style.ScrollbarRounding = 10.0f;
        style.GrabRounding = 6.0f;
        style.TabRounding = 6.0f;

        // Větší dotykové cíle a vzduch (cílovka: žáci)
        style.FramePadding = new Vector2(10.0f, 6.0f);
        style.ItemSpacing = new Vector2(8.0f, 6.0f);
        style.WindowPadding = new Vector2(10.0f, 10.0f);
        style.ScrollbarSize = 14.0f;

        // Okraje
        style.WindowBorderSize = 1.0f;
        style.FrameBorderSize = 1.0f;
        style.PopupBorderSize = 1.0f;

        // Paleta "Tmavá evoluce": WindowBg #14171D, FrameBg #11141A,
        // Button #242A37, Border #2B3140, Text #E8EAF1, akcent zlatá #D9B23F
        var colors = style.Colors;
        colors[(int)ImGuiCol.Text] = new Vector4(0.91f, 0.92f, 0.95f, 1.00f);
        colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.60f, 0.63f, 0.70f, 1.00f);
        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.08f, 0.09f, 0.11f, 1.00f);
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.10f, 0.12f, 0.15f, 1.00f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.11f, 0.13f, 0.16f, 0.98f);
        colors[(int)ImGuiCol.Border] = new Vector4(0.17f, 0.19f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);

        // Vstupy (FrameBg) – tmavší než panel, ať pole "zapadnou"
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.07f, 0.08f, 0.10f, 1.00f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.14f, 0.16f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.17f, 0.19f, 0.25f, 1.00f);

        // Záhlaví oken
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.06f, 0.07f, 0.09f, 1.00f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.10f, 0.12f, 0.15f, 1.00f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.05f, 0.06f, 0.08f, 1.00f);
        colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.06f, 0.07f, 0.09f, 1.00f);

        // Posuvníky
        colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.07f, 0.08f, 0.10f, 0.50f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.14f, 0.16f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.19f, 0.22f, 0.29f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.24f, 0.28f, 0.37f, 1.00f);

        // Zlatý akcent (zachován z původní identity)
        colors[(int)ImGuiCol.CheckMark] = new Vector4(0.85f, 0.70f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.85f, 0.70f, 0.25f, 0.90f);
        colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.94f, 0.82f, 0.47f, 1.00f);

        // Tlačítka
        colors[(int)ImGuiCol.Button] = new Vector4(0.14f, 0.16f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.19f, 0.22f, 0.29f, 1.00f);
        colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.24f, 0.28f, 0.37f, 1.00f);

        // Výběr v Hierarchii / TreeNode / Selectable – zlatý nádech
        colors[(int)ImGuiCol.Header] = new Vector4(0.85f, 0.70f, 0.25f, 0.18f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.85f, 0.70f, 0.25f, 0.28f);
        colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.85f, 0.70f, 0.25f, 0.38f);

        // Oddělovače
        colors[(int)ImGuiCol.Separator] = new Vector4(0.17f, 0.19f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.24f, 0.28f, 0.37f, 1.00f);
        colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.30f, 0.35f, 0.46f, 1.00f);

        // Záložky
        colors[(int)ImGuiCol.Tab] = new Vector4(0.10f, 0.12f, 0.15f, 1.00f);
        colors[(int)ImGuiCol.TabHovered] = new Vector4(0.17f, 0.19f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.TabSelected] = new Vector4(0.14f, 0.16f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.08f, 0.09f, 0.11f, 1.00f);
        colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.11f, 0.13f, 0.16f, 1.00f);

        // Dokování, grafy, výběr textu, drag&drop – zlatá
        colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.85f, 0.70f, 0.25f, 0.30f);
        colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(0.08f, 0.09f, 0.11f, 1.00f);
        colors[(int)ImGuiCol.PlotLines] = new Vector4(0.85f, 0.70f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.94f, 0.82f, 0.47f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.85f, 0.70f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.94f, 0.82f, 0.47f, 1.00f);
        colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.85f, 0.70f, 0.25f, 0.35f);
        colors[(int)ImGuiCol.DragDropTarget] = new Vector4(0.85f, 0.70f, 0.25f, 0.90f);
        colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.00f, 1.00f, 1.00f, 0.70f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.20f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.05f, 0.06f, 0.08f, 0.45f);
    }
}
