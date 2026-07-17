using System.Numerics;
using ImGuiNET;
using MiniEngine.Core;
using MiniEngine.Lessons;

namespace MiniEngine.Editor;

// ============================================================
// Fáze 37 – Panel Lekce: ImGui overlay ve viewportu (styl 1a).
// Stavy: výchozí / nápověda / krok splněn / minimalizovaný pill
// + okno výběru lekcí + obrazovka dokončení.
//
// Napojení v Game.cs:
//   _lessons = new LessonSystem();
//   _lessons.LoadAll("assets/lessons");
//   _lessonPanel = new LessonPanel(_lessons);
//   // update:  _lessons.Update(_world, _selection, dt);
//   // UI fáze (po vykreslení viewportu):
//   //   _lessonPanel.Draw(viewportPos, viewportSize, _world, _selection, _camera);
//   // Tlačítko "Lekce" v toolbaru: _lessonPanel.PickerOpen = true;
// ============================================================

public sealed class LessonPanel
{
    // Paleta "Tmavá evoluce" (1a)
    private static readonly Vector4 Gold = new(0.85f, 0.70f, 0.25f, 1f);
    private static readonly Vector4 GoldBg = new(0.85f, 0.70f, 0.25f, 0.16f);
    private static readonly Vector4 Green = new(0.35f, 0.70f, 0.41f, 1f);
    private static readonly Vector4 Dim = new(0.60f, 0.63f, 0.70f, 1f);
    private static readonly Vector4 Track = new(0.17f, 0.19f, 0.25f, 1f);
    private static readonly Vector4 PanelBg = new(0.06f, 0.07f, 0.09f, 0.95f);

    private readonly LessonSystem _lessons;
    private readonly TeacherMode _teacher;

    public bool PickerOpen;
    public bool Minimized;
    public bool ShowHint;
    private int _hintCountedForStep = -1; // nápovědu počítáme jen 1× na krok

    public LessonPanel(LessonSystem lessons, TeacherMode teacher)
    {
        _lessons = lessons;
        _teacher = teacher;
    }

    public void Draw(Vector2 viewportPos, Vector2 viewportSize, World world, EditorSelection selection, EditorCamera camera)
    {
        if (PickerOpen) DrawPicker(viewportPos, viewportSize);
        if (_lessons.Active == null) return;

        if (_lessons.State == LessonState.LekceDokoncena)
        {
            DrawCompletion(viewportPos, viewportSize);
        }
        else if (Minimized)
        {
            DrawPill(viewportPos);
        }
        else
        {
            DrawTaskPanel(viewportPos, world, selection, camera);
        }
    }

    // ---------- Panel úkolu (levý horní roh viewportu) ----------

    private void DrawTaskPanel(Vector2 viewportPos, World world, EditorSelection selection, EditorCamera camera)
    {
        var lesson = _lessons.Active!;
        var step = _lessons.ActiveStep;
        if (step == null) return;

        bool done = _lessons.State == LessonState.KrokSplnen;
        var accent = done ? Green : Gold;

        ImGui.SetNextWindowPos(viewportPos + new Vector2(12f, 12f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(280f, 0f)); // výška auto
        ImGui.PushStyleColor(ImGuiCol.WindowBg, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, accent);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 12f));

        ImGui.Begin("##LessonTask", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
                                    ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoDocking);

        // Hlavička: LEKCE N · NÁZEV + minimalizace
        ImGui.TextColored(accent, $"LEKCE · {lesson.Nazev.ToUpperInvariant()}");
        ImGui.SameLine(ImGui.GetWindowWidth() - 30f);
        if (ImGui.SmallButton("—")) Minimized = true;

        ImGui.Spacing();
        if (done)
        {
            ImGui.TextColored(Green, "Krok splněn!");
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(step.Text);
            ImGui.PopTextWrapPos();
        }
        else
        {
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(step.Text);
            ImGui.PopTextWrapPos();
        }

        // Rozbalená nápověda
        if (ShowHint && !done && step.Napoveda.Count > 0)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, GoldBg);
            
            float hintHeight = step.Napoveda.Count * ImGui.GetTextLineHeightWithSpacing() + 12f;
            if (_teacher.Settings.ShowSolutionInHelp)
            {
                hintHeight += ImGui.GetTextLineHeightWithSpacing() * 2f + 20f;
            }

            ImGui.BeginChild("##hint", new Vector2(0f, hintHeight), ImGuiChildFlags.None);
            ImGui.Indent(6f);
            for (int i = 0; i < step.Napoveda.Count; i++)
                ImGui.TextWrapped($"{i + 1}. {step.Napoveda[i]}");

            if (_teacher.Settings.ShowSolutionInHelp)
            {
                ImGui.Spacing();
                ImGui.TextColored(Gold, "Technické řešení:");
                string solutionText = step.Podminka.Typ switch
                {
                    "VybranaEntita" => $"Označ entitu s názvem: {step.Podminka.Jmeno}",
                    "MaKomponentu" => $"Entita '{step.Podminka.Jmeno}' musí mít komponentu: {step.Podminka.Komponenta}",
                    "Vlastnost" => $"Nastav vlastnost '{step.Podminka.Cesta}' v rozmezí: {step.Podminka.Min} .. {step.Podminka.Max}",
                    "VzdalenostOd" => $"Přibliž entitu '{step.Podminka.Jmeno}' k '{step.Podminka.Od}' pod {step.Podminka.Vzdalenost}m",
                    _ => "Neznámá podmínka"
                };
                ImGui.TextWrapped(solutionText);
            }

            ImGui.Unindent(6f);
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        // Progres: segment na každý krok
        ImGui.Spacing();
        DrawProgressSegments(lesson.Kroky.Count, _lessons.StepIndex, done);
        ImGui.SameLine();
        ImGui.TextColored(Dim, $"{_lessons.StepIndex + (done ? 1 : 0)}/{lesson.Kroky.Count}");

        // Tlačítka podle stavu
        ImGui.Spacing();
        float w = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        if (done)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Gold);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.09f, 0.11f, 1f));
            if (ImGui.Button("Další krok", new Vector2(ImGui.GetContentRegionAvail().X, 0f))) _lessons.ConfirmStep();
            ImGui.PopStyleColor(2);
        }
        else if (ShowHint)
        {
            if (ImGui.Button("Ukázat kde", new Vector2(w, 0f))) SelectAndFocus(step.CilEntity, world, selection, camera);
            ImGui.SameLine();
            if (ImGui.Button("Skrýt", new Vector2(w, 0f))) ShowHint = false;
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Gold);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.09f, 0.11f, 1f));
            if (ImGui.Button("Nápověda", new Vector2(_teacher.Settings.AllowSkipButton ? w : ImGui.GetContentRegionAvail().X, 0f)))
            {
                ShowHint = true;
                if (_hintCountedForStep != _lessons.StepIndex)
                {
                    _lessons.PouziteNapovedy++;
                    _hintCountedForStep = _lessons.StepIndex;
                    
                    if (_lessons.StepIndex >= 0 && _lessons.StepIndex < _lessons.Progress.NapovedyKroku.Count)
                    {
                        _lessons.Progress.NapovedyKroku[_lessons.StepIndex]++;
                        _lessons.Progress.CelkemNapoved = _lessons.PouziteNapovedy;
                        _lessons.SaveProgress();
                    }
                }
            }
            ImGui.PopStyleColor(2);
            
            if (_teacher.Settings.AllowSkipButton)
            {
                ImGui.SameLine();
                if (ImGui.Button("Přeskočit", new Vector2(w, 0f))) _lessons.SkipStep();
            }
        }

        ImGui.End();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static void DrawProgressSegments(int total, int currentIndex, bool currentDone)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        const float segW = 22f, segH = 4f, gap = 5f;
        for (int i = 0; i < total; i++)
        {
            var col = i < currentIndex ? Gold : (i == currentIndex && currentDone ? Green : Track);
            var p0 = pos + new Vector2(i * (segW + gap), 4f);
            dl.AddRectFilled(p0, p0 + new Vector2(segW, segH), ImGui.ColorConvertFloat4ToU32(col), 2f);
        }
        ImGui.Dummy(new Vector2(total * (segW + gap), 12f));
    }

    private void SelectAndFocus(string cilEntity, World world, EditorSelection selection, EditorCamera camera)
    {
        if (string.IsNullOrEmpty(cilEntity)) return;
        int idx = _lessons.FindEntity(world, cilEntity);
        if (idx < 0) return;

        selection.EntityIndex = idx;
        var transforms = world.Store<Transform>();
        if (transforms.Has(idx))
            camera.Focus(transforms.Get(idx).World.Translation, 6f);
    }

    // ---------- Minimalizovaný pill ----------

    private void DrawPill(Vector2 viewportPos)
    {
        var lesson = _lessons.Active!;
        ImGui.SetNextWindowPos(viewportPos + new Vector2(12f, 12f), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, PanelBg);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 20f);

        ImGui.Begin("##LessonPill", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
                                    ImGuiWindowFlags.NoDocking);

        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        dl.AddCircleFilled(p + new Vector2(5f, ImGui.GetTextLineHeight() * 0.5f + 1f), 4f, ImGui.ColorConvertFloat4ToU32(Gold));
        ImGui.Dummy(new Vector2(12f, 0f)); ImGui.SameLine();
        ImGui.TextUnformatted(lesson.Nazev);
        ImGui.SameLine();
        ImGui.TextColored(Dim, $"{_lessons.StepIndex}/{lesson.Kroky.Count}");
        ImGui.SameLine();
        if (ImGui.SmallButton(">")) Minimized = false;

        ImGui.End();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    // ---------- Okno výběru lekcí ----------

    private void DrawPicker(Vector2 viewportPos, Vector2 viewportSize)
    {
        ImGui.SetNextWindowPos(viewportPos + viewportSize * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(470f, 0f), ImGuiCond.Appearing);

        if (ImGui.Begin("Lekce — Základy 3D tvorby", ref PickerOpen,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse))
        {
            int doneCount = 0;
            for (int i = 0; i < _lessons.Lessons.Count; i++)
                if (_lessons.IsCompleted(_lessons.Lessons[i])) doneCount++;

            ImGui.TextColored(Dim, "Postup kurzem");
            ImGui.SameLine(ImGui.GetWindowWidth() - 70f);
            ImGui.TextColored(Gold, $"{doneCount} z {_lessons.Lessons.Count}");
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Gold);
            ImGui.ProgressBar(_lessons.Lessons.Count > 0 ? (float)doneCount / _lessons.Lessons.Count : 0f, new Vector2(-1f, 6f), "");
            ImGui.PopStyleColor();
            ImGui.Separator();

            for (int i = 0; i < _lessons.Lessons.Count; i++)
            {
                var l = _lessons.Lessons[i];
                bool completed = _lessons.IsCompleted(l);
                bool unlocked = _lessons.IsUnlocked(i);
                ImGui.PushID(i);

                if (completed)
                {
                    ImGui.TextColored(Green, "  •");
                    ImGui.SameLine(34f);
                    ImGui.TextColored(Dim, l.Nazev);
                    ImGui.SameLine(ImGui.GetWindowWidth() - 70f);
                    ImGui.TextColored(Green, "Hotovo");
                }
                else if (unlocked)
                {
                    ImGui.TextColored(Gold, $"  {i + 1}");
                    ImGui.SameLine(34f);
                    ImGui.TextUnformatted(l.Nazev);
                    ImGui.SameLine(ImGui.GetWindowWidth() - 110f);
                    ImGui.PushStyleColor(ImGuiCol.Button, Gold);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.09f, 0.11f, 1f));
                    if (ImGui.SmallButton(_lessons.Progress.AktualniLekce == l.Id ? "Pokračovat" : "Spustit"))
                    {
                        _lessons.StartLesson(l);
                        PickerOpen = false;
                        Minimized = false;
                        ShowHint = false;
                    }
                    ImGui.PopStyleColor(2);
                }
                else
                {
                    ImGui.TextDisabled($"  {i + 1}");
                    ImGui.SameLine(34f);
                    ImGui.TextDisabled(l.Nazev);
                    ImGui.SameLine(ImGui.GetWindowWidth() - 80f);
                    ImGui.TextDisabled("Zamčeno");
                }
                ImGui.PopID();
            }

            ImGui.Separator();
            ImGui.TextColored(Dim, "Lekce se odemykají postupně.");
            ImGui.SameLine();
            ImGui.Checkbox("Odemknout vše (učitel)", ref _lessons.OdemknoutVse);
        }
        ImGui.End();
    }

    // ---------- Dokončení lekce ----------

    private void DrawCompletion(Vector2 viewportPos, Vector2 viewportSize)
    {
        var lesson = _lessons.Active!;
        ImGui.SetNextWindowPos(viewportPos + viewportSize * 0.5f, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(390f, 0f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, Gold);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(22f, 22f));

        ImGui.Begin("##LessonDone", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
                                    ImGuiWindowFlags.NoDocking);

        ImGui.SetWindowFontScale(1.25f);
        CenteredText($"Lekce dokončena!");
        ImGui.SetWindowFontScale(1f);
        CenteredTextColored(Dim, lesson.Nazev);
        ImGui.Spacing();

        int min = (int)(_lessons.CasLekce / 60f);
        int sec = (int)(_lessons.CasLekce % 60f);
        CenteredTextColored(Gold, $"Čas {min}:{sec:00}   ·   Kroků {lesson.Kroky.Count}/{lesson.Kroky.Count}   ·   Nápověd {_lessons.PouziteNapovedy}");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Další lekce = první odemčená nedokončená
        LessonData? next = null;
        for (int i = 0; i < _lessons.Lessons.Count; i++)
            if (!_lessons.IsCompleted(_lessons.Lessons[i]) && _lessons.IsUnlocked(i)) { next = _lessons.Lessons[i]; break; }

        float w = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        ImGui.BeginDisabled(next == null);
        ImGui.PushStyleColor(ImGuiCol.Button, Gold);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.09f, 0.11f, 1f));
        if (ImGui.Button(next != null ? $"Další lekce" : "Vše hotovo", new Vector2(w, 0f)) && next != null)
        {
            _lessons.StartLesson(next);
            ShowHint = false;
        }
        ImGui.PopStyleColor(2);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Přehled lekcí", new Vector2(w, 0f)))
        {
            _lessons.CloseLesson();
            PickerOpen = true;
        }

        ImGui.End();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static void CenteredText(string text)
    {
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - ImGui.CalcTextSize(text).X) * 0.5f);
        ImGui.TextUnformatted(text);
    }

    private static void CenteredTextColored(Vector4 col, string text)
    {
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - ImGui.CalcTextSize(text).X) * 0.5f);
        ImGui.TextColored(col, text);
    }
}
