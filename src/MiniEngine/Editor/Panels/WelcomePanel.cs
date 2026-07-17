using System.Numerics;
using ImGuiNET;
using MiniEngine.Lessons;
using System;

namespace MiniEngine.Editor.Panels;

public sealed class WelcomePanel
{
    private readonly LessonSystem _lessonSystem;
    private string _tempName = "";
    private string _tempFolder = "";
    private bool _tempIsBeginner = true;

    public bool Open { get; set; }
    public Action? OnCompleted { get; set; }

    public WelcomePanel(LessonSystem lessonSystem)
    {
        _lessonSystem = lessonSystem;
    }

    public void OpenPanel()
    {
        _tempName = _lessonSystem.StudentName ?? "";
        _tempFolder = _lessonSystem.ClassFolder ?? "";
        _tempIsBeginner = _lessonSystem.LayoutMode != "Pokrocily";
        Open = true;
    }

    public void Draw()
    {
        var viewport = ImGui.GetMainViewport();

        // 1. Tmavé pozadí přes celou obrazovku
        ImGui.SetNextWindowPos(viewport.WorkPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(viewport.WorkSize, ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.10f, 1.0f));
        ImGui.Begin("WelcomeBackdrop", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings);
        ImGui.End();
        ImGui.PopStyleColor();

        // 2. Matematicky vycentrovaný kompaktní dialogový box
        float formWidth = 500f;
        float formHeight = 500f;
        float windowPaddingX = 25f;
        float contentWidth = formWidth - (windowPaddingX * 2f); // 450f
        Vector2 formSize = new Vector2(formWidth, formHeight);
        Vector2 formPos = viewport.WorkPos + (viewport.WorkSize - formSize) / 2f;

        ImGui.SetNextWindowPos(formPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(formSize, ImGuiCond.Always);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(windowPaddingX, 20f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.12f, 0.12f, 0.14f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.20f, 0.20f, 0.22f, 1.0f));

        // Začátek samotného dialogového okna (s vypnutým scrollováním a bez ukládání pozice)
        ImGui.Begin("WelcomeDialog", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings);
        {
            // Velký nadpis uvnitř dialogu
            ImGui.SetWindowFontScale(1.5f);
            string title = "Vítej v MiniEngine!";
            float titleWidth = ImGui.CalcTextSize(title).X;
            ImGui.SetCursorPosX((contentWidth - titleWidth) / 2f);
            ImGui.TextColored(new Vector4(0.85f, 0.70f, 0.25f, 1.0f), title);
            ImGui.SetWindowFontScale(1.0f);

            // Podnadpis
            string subtitle = "Jednoduchý 3D editor pro výuku a minihry";
            float subWidth = ImGui.CalcTextSize(subtitle).X;
            ImGui.SetCursorPosX((contentWidth - subWidth) / 2f);
            ImGui.TextDisabled(subtitle);

            ImGui.Dummy(new Vector2(0f, 4f));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0f, 8f));

            ImGui.PushItemWidth(contentWidth);

            // Uživatelské jméno
            ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.88f, 1.0f), "Tvoje jméno a příjmení:");
            ImGui.InputText("##NameInput", ref _tempName, 64);

            ImGui.Dummy(new Vector2(0f, 6f));

            // Složka třídy
            ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.88f, 1.0f), "Složka třídy (sdílený disk pro učitele):");
            ImGui.InputText("##FolderInput", ref _tempFolder, 128);
            ImGui.TextDisabled("Příklad: assets/vyuka/9B");

            ImGui.Dummy(new Vector2(0f, 6f));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0f, 6f));

            // Volba úrovně rozhraní
            ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.88f, 1.0f), "Zvol si úroveň rozhraní editoru:");
            ImGui.Spacing();

            if (ImGui.RadioButton("Začátečník (Zjednodušené rozhraní)", _tempIsBeginner))
            {
                _tempIsBeginner = true;
            }
            ImGui.TextDisabled("   Skryje složitá nastavení (Node Editor, Profiler atd.).");

            ImGui.Dummy(new Vector2(0f, 4f));
            if (ImGui.RadioButton("Pokročilý (Plné vývojářské rozhraní)", !_tempIsBeginner))
            {
                _tempIsBeginner = false;
            }
            ImGui.TextDisabled("   Zpřístupní všechny nástroje, ladičky a efekty.");

            ImGui.Dummy(new Vector2(0f, 15f));

            // Spouštěcí tlačítko
            float btnWidth = 220f;
            ImGui.SetCursorPosX((contentWidth - btnWidth) / 2f);

            bool isNameInvalid = string.IsNullOrWhiteSpace(_tempName);
            if (isNameInvalid)
            {
                ImGui.BeginDisabled();
            }

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.85f, 0.70f, 0.25f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.92f, 0.78f, 0.35f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.75f, 0.60f, 0.18f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.08f, 0.10f, 1.0f));

            if (ImGui.Button("Vstoupit do editoru", new Vector2(btnWidth, 36f)))
            {
                string layout = _tempIsBeginner ? "Zacatecnik" : "Pokrocily";
                _lessonSystem.SaveStudentSettings(_tempName.Trim(), _tempFolder.Trim(), layout);
                Open = false;
                OnCompleted?.Invoke();
            }
            ImGui.PopStyleColor(4);

            if (isNameInvalid)
            {
                ImGui.EndDisabled();
                ImGui.Spacing();
                string errText = "Zadej prosím své jméno!";
                float errWidth = ImGui.CalcTextSize(errText).X;
                ImGui.SetCursorPosX((contentWidth - errWidth) / 2f);
                ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 1.0f), errText);
            }

            ImGui.PopItemWidth();
        }
        ImGui.End();

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }
}
