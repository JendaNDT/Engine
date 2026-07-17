using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace MiniEngine.Lessons;

public sealed class TeacherSettings
{
    public string Pin { get; set; } = "1234";
    public string ClassFolder { get; set; } = "";
    public bool UnlockAllLessons { get; set; }
    public bool AllowSkipButton { get; set; } = true;
    public bool ShowSolutionInHelp { get; set; }
    public float FontSizeScale { get; set; } = 1.0f;
}

[JsonSerializable(typeof(TeacherSettings))]
internal partial class TeacherJsonContext : JsonSerializerContext { }

public sealed class TeacherMode
{
    private const string SettingsPath = "teacher.json";

    public TeacherSettings Settings = new();
    public List<StudentProgress> ClassProgress = [];
    public bool Unlocked;
    public string ActiveStudentDetailSafeName = "";

    private readonly LessonSystem _lessons;
    private float _scanTimer = 5.0f; // initial scan immediately

    public TeacherMode(LessonSystem lessons)
    {
        _lessons = lessons;
        LoadSettings();
    }

    public void LoadSettings()
    {
        if (File.Exists(SettingsPath))
        {
            try
            {
                Settings = JsonSerializer.Deserialize(File.ReadAllText(SettingsPath),
                    TeacherJsonContext.Default.TeacherSettings) ?? new TeacherSettings();
                
                // Synchronize lesson system settings
                _lessons.OdemknoutVse = Settings.UnlockAllLessons;
            }
            catch { }
        }
    }

    public void SaveSettings()
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings,
                TeacherJsonContext.Default.TeacherSettings));
            
            // Synchronize
            _lessons.OdemknoutVse = Settings.UnlockAllLessons;
        }
        catch { }
    }

    public void Update(float dt)
    {
        if (!Unlocked || string.IsNullOrEmpty(Settings.ClassFolder)) return;

        _scanTimer += dt;
        if (_scanTimer >= 5.0f)
        {
            _scanTimer = 0f;
            ScanClassFolder();
        }
    }

    public void ScanClassFolder()
    {
        if (string.IsNullOrEmpty(Settings.ClassFolder) || !Directory.Exists(Settings.ClassFolder)) return;

        try
        {
            var files = Directory.GetFiles(Settings.ClassFolder, "*.json");
            var newList = new List<StudentProgress>();

            foreach (var file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.Equals("teacher", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("student", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    string content = File.ReadAllText(file);
                    var progress = JsonSerializer.Deserialize(content, LessonJsonContext.Default.LessonProgress);
                    if (progress == null) continue;

                    var sp = new StudentProgress
                    {
                        Name = DeSanitizeName(fileName),
                        SafeName = fileName,
                        DoneLessons = progress.Dokoncene,
                        TotalTimeSeconds = progress.CelkovyCas,
                        TotalHints = progress.CelkemNapoved,
                        LastActive = File.GetLastWriteTime(file)
                    };

                    if (!string.IsNullOrEmpty(progress.AktualniLekce))
                    {
                        var lesson = _lessons.Lessons.FirstOrDefault(l => l.Id == progress.AktualniLekce);
                        if (lesson != null)
                        {
                            sp.LessonId = lesson.Id;
                            sp.LessonName = lesson.Nazev;
                            sp.StepIndex = progress.AktualniKrok;
                            sp.TotalSteps = lesson.Kroky.Count;
                            
                            sp.StepTexts = lesson.Kroky.Select(k => k.Text).ToList();
                            sp.StepTimes = progress.CasyKroku;
                            sp.StepHints = progress.NapovedyKroku;

                            while (sp.StepTimes.Count < lesson.Kroky.Count) sp.StepTimes.Add(0.0);
                            while (sp.StepHints.Count < lesson.Kroky.Count) sp.StepHints.Add(0);

                            if (progress.AktualniKrok >= 0 && progress.AktualniKrok < lesson.Kroky.Count)
                            {
                                sp.StepTimeSeconds = sp.StepTimes[progress.AktualniKrok];
                                int currentStepHints = sp.StepHints[progress.AktualniKrok];

                                // Heuristics: stuck if spent > 8 mins (480s) on current step and used 0 hints
                                bool isStuck = (sp.StepTimeSeconds > 480.0) && (currentStepHints == 0);
                                sp.Status = isStuck ? "Zaseknutý" : "Pracuje";
                            }
                        }
                    }
                    else if (progress.Dokoncene.Count > 0)
                    {
                        sp.Status = "Hotovo";
                    }
                    else
                    {
                        sp.Status = "Pracuje";
                    }

                    // Check if inactive for more than 10 minutes
                    if (DateTime.Now - sp.LastActive > TimeSpan.FromMinutes(10))
                    {
                        sp.Status = "Nepřipojen";
                    }

                    newList.Add(sp);
                }
                catch { }
            }

            ClassProgress = newList;
        }
        catch { }
    }

    public void ResetStudent(string safeName)
    {
        if (string.IsNullOrEmpty(Settings.ClassFolder)) return;
        string path = Path.Combine(Settings.ClassFolder, $"{safeName}.json");
        if (File.Exists(path))
        {
            try
            {
                var blank = new LessonProgress();
                blank.PosledniZmenaUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(path, JsonSerializer.Serialize(blank, LessonJsonContext.Default.LessonProgress));
                ScanClassFolder();
            }
            catch { }
        }
    }

    private static string DeSanitizeName(string safeName)
    {
        return safeName.Replace('-', ' ');
    }
}

public sealed class StudentProgress
{
    public string Name { get; set; } = "";
    public string SafeName { get; set; } = "";
    public string LessonId { get; set; } = "";
    public string LessonName { get; set; } = "";
    public int StepIndex { get; set; }
    public int TotalSteps { get; set; }
    public double TotalTimeSeconds { get; set; }
    public double StepTimeSeconds { get; set; }
    public int TotalHints { get; set; }
    public DateTime LastActive { get; set; }
    public string Status { get; set; } = "Pracuje"; // "Pracuje", "Zaseknutý", "Hotovo", "Nepřipojen"
    public List<string> DoneLessons { get; set; } = [];
    public List<double> StepTimes { get; set; } = [];
    public List<int> StepHints { get; set; } = [];
    public List<string> StepTexts { get; set; } = [];
}
