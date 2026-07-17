using System;
using System.IO;
using System.Text.Json;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using MiniEngine.Core;
using MiniEngine.Lessons;

namespace MiniEngine.Editor.Panels;

public sealed class TeacherPanel
{
    private static readonly Vector4 Gold = new(0.85f, 0.70f, 0.25f, 1f);
    private static readonly Vector4 GoldBg = new(0.85f, 0.70f, 0.25f, 0.16f);
    private static readonly Vector4 Green = new(0.35f, 0.70f, 0.41f, 1f);
    private static readonly Vector4 Red = new(0.88f, 0.32f, 0.32f, 1f);
    private static readonly Vector4 Dim = new(0.60f, 0.63f, 0.70f, 1f);
    private static readonly Vector4 PanelBg = new(0.06f, 0.07f, 0.09f, 0.95f);

    private readonly TeacherMode _teacherMode;
    public bool Open;

    // Login state
    private string _pinInput = "";
    private string _loginError = "";

    // Settings state variables
    private string _classFolderInput = "";
    private string _newPinInput = "";
    private int _selectedStudentResetIndex = 0;

    // Overview state
    private string _sortBy = "Stav"; // "Stav", "Jméno", "Lekce"
    private StudentProgress? _selectedStudentDetail;

    public TeacherPanel(TeacherMode teacherMode)
    {
        _teacherMode = teacherMode;
        _classFolderInput = _teacherMode.Settings.ClassFolder;
    }

    public void Draw()
    {
        if (!Open) return;

        // Apply font scale based on settings
        ImGui.GetIO().FontGlobalScale = _teacherMode.Settings.FontSizeScale;

        if (!_teacherMode.Unlocked)
        {
            DrawLogin();
        }
        else
        {
            DrawDashboard();
        }

        // Reset font scale
        ImGui.GetIO().FontGlobalScale = 1.0f;
    }

    private void DrawLogin()
    {
        var viewportSize = new Vector2(Raylib_cs.Raylib.GetScreenWidth(), Raylib_cs.Raylib.GetScreenHeight());
        ImGui.SetNextWindowPos(viewportSize * 0.5f, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(300f, 160f));

        ImGui.PushStyleColor(ImGuiCol.WindowBg, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, Gold);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);

        if (ImGui.Begin("Režim učitele – Přihlášení", ref Open, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.Text("Zadejte PIN učitele:");
            ImGui.Spacing();

            ImGui.PushItemWidth(-1);
            if (ImGui.InputText("##PIN", ref _pinInput, 8, ImGuiInputTextFlags.Password | ImGuiInputTextFlags.EnterReturnsTrue))
            {
                TryLogin();
            }
            ImGui.PopItemWidth();

            if (_loginError.Length > 0)
            {
                ImGui.TextColored(Red, _loginError);
            }

            ImGui.Spacing();
            float btnW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;

            ImGui.PushStyleColor(ImGuiCol.Button, Gold);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.09f, 0.11f, 1f));
            if (ImGui.Button("Vstoupit", new Vector2(btnW, 0f)))
            {
                TryLogin();
            }
            ImGui.PopStyleColor(2);

            ImGui.SameLine();
            if (ImGui.Button("Zrušit", new Vector2(btnW, 0f)))
            {
                Open = false;
            }
        }
        ImGui.End();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }

    private void TryLogin()
    {
        if (_pinInput == _teacherMode.Settings.Pin)
        {
            _teacherMode.Unlocked = true;
            _loginError = "";
            _pinInput = "";
            _teacherMode.ScanClassFolder();
        }
        else
        {
            _loginError = "Nesprávný PIN!";
        }
    }

    private void DrawDashboard()
    {
        var viewportSize = new Vector2(Raylib_cs.Raylib.GetScreenWidth(), Raylib_cs.Raylib.GetScreenHeight());
        ImGui.SetNextWindowPos(viewportSize * 0.5f, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(800f, 500f), ImGuiCond.Appearing);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, Gold);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);

        if (ImGui.Begin("Režim učitele – Administrace", ref Open, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
        {
            if (ImGui.BeginTabBar("TeacherTabBar"))
            {
                if (ImGui.BeginTabItem("Přehled třídy"))
                {
                    DrawOverview();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Nastavení výuky"))
                {
                    DrawSettings();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);

        // Render detail popup if active
        if (_selectedStudentDetail != null)
        {
            DrawStudentDetail();
        }
    }

    private void DrawOverview()
    {
        ImGui.TextColored(Dim, $"Složka třídy: {_teacherMode.Settings.ClassFolder}");
        ImGui.SameLine();
        if (ImGui.Button("Obnovit"))
        {
            _teacherMode.ScanClassFolder();
        }

        ImGui.Separator();
        ImGui.Spacing();

        // Stats summary
        int working = _teacherMode.ClassProgress.Count(p => p.Status == "Pracuje");
        int stuck = _teacherMode.ClassProgress.Count(p => p.Status == "Zaseknutý");
        int done = _teacherMode.ClassProgress.Count(p => p.Status == "Hotovo");
        int offline = _teacherMode.ClassProgress.Count(p => p.Status == "Nepřipojen");

        ImGui.Text("Celkem žáků: "); ImGui.SameLine(); ImGui.TextColored(Gold, $"{_teacherMode.ClassProgress.Count}"); ImGui.SameLine();
        ImGui.Text(" | Pracuje: "); ImGui.SameLine(); ImGui.TextColored(Green, $"{working}"); ImGui.SameLine();
        ImGui.Text(" | Zaseknutí: "); ImGui.SameLine(); ImGui.TextColored(Red, $"{stuck}"); ImGui.SameLine();
        ImGui.Text(" | Hotovo: "); ImGui.SameLine(); ImGui.TextColored(Gold, $"{done}");

        ImGui.Spacing();

        // Sort Combo
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Řadit podle:"); ImGui.SameLine();
        string[] sortOptions = { "Stav", "Jméno", "Lekce" };
        if (ImGui.BeginCombo("##SortBy", _sortBy))
        {
            for (int i = 0; i < sortOptions.Length; i++)
            {
                bool isSelected = (_sortBy == sortOptions[i]);
                if (ImGui.Selectable(sortOptions[i], isSelected))
                {
                    _sortBy = sortOptions[i];
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();

        // Student Table
        var list = _teacherMode.ClassProgress.ToList();
        if (_sortBy == "Stav")
        {
            list = list.OrderBy(p => p.Status == "Zaseknutý" ? 0 : (p.Status == "Pracuje" ? 1 : (p.Status == "Hotovo" ? 2 : 3))).ToList();
        }
        else if (_sortBy == "Jméno")
        {
            list = list.OrderBy(p => p.Name).ToList();
        }
        else if (_sortBy == "Lekce")
        {
            list = list.OrderBy(p => p.LessonName).ToList();
        }

        if (ImGui.BeginTable("StudentsTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0f, 280f)))
        {
            ImGui.TableSetupColumn("JMÉNO");
            ImGui.TableSetupColumn("LEKCE");
            ImGui.TableSetupColumn("POSTUP");
            ImGui.TableSetupColumn("ČAS");
            ImGui.TableSetupColumn("NÁPOV.");
            ImGui.TableSetupColumn("STAV");
            ImGui.TableHeadersRow();

            foreach (var student in list)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                
                if (ImGui.Selectable(student.Name, false, ImGuiSelectableFlags.SpanAllColumns))
                {
                    _selectedStudentDetail = student;
                }
                
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(string.IsNullOrEmpty(student.LessonName) ? "—" : student.LessonName);

                ImGui.TableNextColumn();
                if (student.TotalSteps > 0)
                    ImGui.TextUnformatted($"krok {student.StepIndex + 1}/{student.TotalSteps}");
                else
                    ImGui.TextUnformatted("—");

                ImGui.TableNextColumn();
                int min = (int)(student.TotalTimeSeconds / 60.0);
                ImGui.TextUnformatted($"{min} m");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{student.TotalHints}");

                ImGui.TableNextColumn();
                Vector4 statusColor = Dim;
                if (student.Status == "Zaseknutý") statusColor = Red;
                else if (student.Status == "Pracuje") statusColor = Green;
                else if (student.Status == "Hotovo") statusColor = Gold;

                ImGui.TextColored(statusColor, student.Status);
            }
            ImGui.EndTable();
        }
    }

    private void DrawStudentDetail()
    {
        if (_selectedStudentDetail == null) return;

        var currentData = _teacherMode.ClassProgress.FirstOrDefault(p => p.SafeName == _selectedStudentDetail.SafeName);
        if (currentData != null)
        {
            _selectedStudentDetail = currentData;
        }

        var student = _selectedStudentDetail;

        var viewportSize = new Vector2(Raylib_cs.Raylib.GetScreenWidth(), Raylib_cs.Raylib.GetScreenHeight());
        ImGui.SetNextWindowPos(viewportSize * 0.5f, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(450f, 400f));

        ImGui.PushStyleColor(ImGuiCol.WindowBg, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, Gold);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);

        string title = $"Detail žáka: {student.Name}";
        if (ImGui.Begin(title, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoDocking))
        {
            Vector4 statusColor = Dim;
            if (student.Status == "Zaseknutý") statusColor = Red;
            else if (student.Status == "Pracuje") statusColor = Green;
            else if (student.Status == "Hotovo") statusColor = Gold;

            ImGui.Text("Stav: "); ImGui.SameLine(); ImGui.TextColored(statusColor, student.Status);
            ImGui.SameLine(ImGui.GetWindowWidth() - 35f);
            if (ImGui.Button("✕"))
            {
                _selectedStudentDetail = null;
            }

            ImGui.TextColored(Dim, $"{student.LessonName} | celkem {(int)(student.TotalTimeSeconds / 60.0)} min | {student.TotalHints} nápověd");
            ImGui.Separator();
            ImGui.Spacing();

            // Steps list
            if (ImGui.BeginChild("StepsList", new Vector2(0f, 200f), ImGuiChildFlags.Borders))
            {
                for (int i = 0; i < student.StepTexts.Count; i++)
                {
                    bool isDone = i < student.StepIndex;
                    bool isCurrent = i == student.StepIndex;

                    if (isDone)
                    {
                        double sTime = student.StepTimes.ElementAtOrDefault(i);
                        int sHints = student.StepHints.ElementAtOrDefault(i);
                        ImGui.TextColored(Green, $"[✓] {student.StepTexts[i]} ({sHints} nap., {sTime / 60.0:F1} min)");
                    }
                    else if (isCurrent)
                    {
                        double sTime = student.StepTimes.ElementAtOrDefault(i);
                        int sHints = student.StepHints.ElementAtOrDefault(i);
                        ImGui.TextColored(Gold, $"[{i + 1}] {student.StepTexts[i]} ({sHints} nap., {sTime / 60.0:F1} min)  ←");
                    }
                    else
                    {
                        ImGui.TextDisabled($"[ ] {student.StepTexts[i]}");
                    }
                }
                ImGui.EndChild();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            float w = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;

            // Remote controls
            ImGui.BeginDisabled(student.Status == "Hotovo" || student.Status == "Nepřipojen" || student.StepIndex >= student.TotalSteps - 1);
            if (ImGui.Button("Odemknout další krok", new Vector2(w, 0f)))
            {
                RemoteSkipStep(student.SafeName, student.StepIndex + 1);
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Resetovat lekci", new Vector2(w, 0f)))
            {
                RemoteResetLesson(student.SafeName);
            }
        }
        ImGui.End();

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }

    private void RemoteSkipStep(string safeName, int nextStepIndex)
    {
        if (string.IsNullOrEmpty(_teacherMode.Settings.ClassFolder)) return;
        string path = Path.Combine(_teacherMode.Settings.ClassFolder, $"{safeName}.json");
        if (File.Exists(path))
        {
            try
            {
                string content = File.ReadAllText(path);
                var progress = JsonSerializer.Deserialize<LessonProgress>(content, LessonJsonContext.Default.LessonProgress);
                if (progress != null)
                {
                    progress.AktualniKrok = nextStepIndex;
                    progress.PosledniZmenaUtc = DateTime.UtcNow.ToString("o");
                    while (progress.CasyKroku.Count <= nextStepIndex) progress.CasyKroku.Add(0.0);
                    while (progress.NapovedyKroku.Count <= nextStepIndex) progress.NapovedyKroku.Add(0);

                    File.WriteAllText(path, JsonSerializer.Serialize(progress, LessonJsonContext.Default.LessonProgress));
                    _teacherMode.ScanClassFolder();
                }
            }
            catch { }
        }
    }

    private void RemoteResetLesson(string safeName)
    {
        if (string.IsNullOrEmpty(_teacherMode.Settings.ClassFolder)) return;
        string path = Path.Combine(_teacherMode.Settings.ClassFolder, $"{safeName}.json");
        if (File.Exists(path))
        {
            try
            {
                string content = File.ReadAllText(path);
                var progress = JsonSerializer.Deserialize<LessonProgress>(content, LessonJsonContext.Default.LessonProgress);
                if (progress != null)
                {
                    progress.AktualniKrok = 0;
                    progress.CelkovyCas = 0.0;
                    progress.CelkemNapoved = 0;
                    progress.CasyKroku.Clear();
                    progress.NapovedyKroku.Clear();
                    progress.PosledniZmenaUtc = DateTime.UtcNow.ToString("o");

                    File.WriteAllText(path, JsonSerializer.Serialize(progress, LessonJsonContext.Default.LessonProgress));
                    _teacherMode.ScanClassFolder();
                }
            }
            catch { }
        }
    }

    private void DrawSettings()
    {
        ImGui.TextColored(Gold, "PARAMETRY VÝUKY:");
        
        bool unlockAll = _teacherMode.Settings.UnlockAllLessons;
        if (ImGui.Checkbox("Odemknout všechny lekce", ref unlockAll))
        {
            _teacherMode.Settings.UnlockAllLessons = unlockAll;
            _teacherMode.SaveSettings();
        }
        
        bool allowSkip = _teacherMode.Settings.AllowSkipButton;
        if (ImGui.Checkbox("Povolit tlačítko „Přeskočit“", ref allowSkip))
        {
            _teacherMode.Settings.AllowSkipButton = allowSkip;
            _teacherMode.SaveSettings();
        }
        
        bool showSolution = _teacherMode.Settings.ShowSolutionInHelp;
        if (ImGui.Checkbox("Zobrazit řešení přímo v nápovědě", ref showSolution))
        {
            _teacherMode.Settings.ShowSolutionInHelp = showSolution;
            _teacherMode.SaveSettings();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(Gold, "PROJEKTOR:");
        float currentScale = _teacherMode.Settings.FontSizeScale;
        string[] scalesText = { "Výchozí (x1.0)", "Střední (x1.2)", "Velké pro promítání (x1.4)" };
        float[] scales = { 1.0f, 1.2f, 1.4f };
        int selectIdx = Array.IndexOf(scales, currentScale);
        if (selectIdx < 0) selectIdx = 0;

        if (ImGui.Combo("Velikost písma", ref selectIdx, scalesText, scalesText.Length))
        {
            _teacherMode.Settings.FontSizeScale = scales[selectIdx];
            _teacherMode.SaveSettings();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(Gold, "SLOŽKA TŘÍDY:");
        ImGui.InputText("##ClassFolder", ref _classFolderInput, 256);
        ImGui.SameLine();
        if (ImGui.Button("Uložit složku"))
        {
            _teacherMode.Settings.ClassFolder = _classFolderInput;
            _teacherMode.SaveSettings();
            _teacherMode.ScanClassFolder();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(Gold, "RESET ŽÁKA:");
        var studentNames = _teacherMode.ClassProgress.Select(p => p.Name).ToList();
        if (studentNames.Count > 0)
        {
            if (_selectedStudentResetIndex >= studentNames.Count) _selectedStudentResetIndex = 0;
            string selectedResetLabel = studentNames[_selectedStudentResetIndex];
            if (ImGui.BeginCombo("##StudentResetCombo", selectedResetLabel))
            {
                for (int i = 0; i < studentNames.Count; i++)
                {
                    bool isSelected = (i == _selectedStudentResetIndex);
                    if (ImGui.Selectable(studentNames[i], isSelected))
                    {
                        _selectedStudentResetIndex = i;
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, Red);
            if (ImGui.Button("Reset"))
            {
                string safeName = _teacherMode.ClassProgress[_selectedStudentResetIndex].SafeName;
                _teacherMode.ResetStudent(safeName);
            }
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextDisabled("Žádní žáci ve složce nejsou připojeni.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(Gold, "ZABEZPEČENÍ:");
        ImGui.InputText("Nový PIN", ref _newPinInput, 8, ImGuiInputTextFlags.Password);
        ImGui.SameLine();
        if (ImGui.Button("Změnit PIN"))
        {
            if (_newPinInput.Length >= 4)
            {
                _teacherMode.Settings.Pin = _newPinInput;
                _teacherMode.SaveSettings();
                _newPinInput = "";
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("Uzamknout administraci"))
        {
            _teacherMode.Unlocked = false;
        }
    }
}
