using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniEngine.Core;
using MiniEngine.Editor;

namespace MiniEngine.Lessons;

// ============================================================
// Fáze 37 – Panel Lekce: datový model + vyhodnocování podmínek
// Lekce žijí v assets/lessons/*.json, postup v lesson_progress.json.
// Vše AOT-safe: source-generated JSON, žádná reflexe (podmínky
// se vyhodnocují explicitním switchem).
// ============================================================

public sealed class LessonData
{
    public string Id { get; set; } = "";
    public string Nazev { get; set; } = "";
    public string Popis { get; set; } = "";
    public List<LessonStep> Kroky { get; set; } = [];
}

public sealed class LessonStep
{
    public string Text { get; set; } = "";
    /// <summary>Řádky nápovědy (zobrazí se po kliknutí na "Nápověda").</summary>
    public List<string> Napoveda { get; set; } = [];
    /// <summary>Jméno entity pro "Ukázat kde" (výběr + Focus kamery).</summary>
    public string CilEntity { get; set; } = "";
    public LessonCondition Podminka { get; set; } = new();
}

/// <summary>
/// Podmínka splnění kroku. Typy:
///  "VybranaEntita"  – v editoru je vybraná entita jménem Jmeno
///  "MaKomponentu"   – entita Jmeno má komponentu Komponenta ("Light", "ParticleEmitter", "AudioSource", "Behavior", "RigidBody")
///  "Vlastnost"      – číselná vlastnost na Cesta je v rozsahu Min..Max (viz ReadProperty)
///  "VzdalenostOd"   – entita Jmeno je blíž než Vzdalenost od entity Od
/// </summary>
public sealed class LessonCondition
{
    public string Typ { get; set; } = "";
    public string Jmeno { get; set; } = "";
    public string Od { get; set; } = "";
    public string Komponenta { get; set; } = "";
    public string Cesta { get; set; } = "";
    public float Min { get; set; } = float.MinValue;
    public float Max { get; set; } = float.MaxValue;
    public float Vzdalenost { get; set; } = 2f;
}

public sealed class LessonProgress
{
    public List<string> Dokoncene { get; set; } = [];
    public string AktualniLekce { get; set; } = "";
    public int AktualniKrok { get; set; }
    
    // Rozšíření pro Režim učitele (Fáze 38)
    public double CelkovyCas { get; set; }
    public List<double> CasyKroku { get; set; } = [];
    public int CelkemNapoved { get; set; }
    public List<int> NapovedyKroku { get; set; } = [];
    public string PosledniZmenaUtc { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LessonData))]
[JsonSerializable(typeof(LessonProgress))]
internal partial class LessonJsonContext : JsonSerializerContext { }

public enum LessonState { Zadna, Bezi, KrokSplnen, LekceDokoncena }

public sealed class LessonSystem
{
    private const string ProgressPath = "lesson_progress.json";
    private const float KrokSplnenProdleva = 2.0f; // zelený stav drží 2 s, pak sám pokračuje

    public readonly List<LessonData> Lessons = [];
    public LessonProgress Progress = new();

    public LessonData? Active;
    public int StepIndex;
    public LessonState State = LessonState.Zadna;

    // Statistiky pro obrazovku dokončení
    public float CasLekce;
    public int PouziteNapovedy;

    /// <summary>Učitelský režim – zpřístupní všechny lekce bez ohledu na postup.</summary>
    public bool OdemknoutVse;

    // Nastavení žáka (student.json)
    public string StudentName = "";
    public string ClassFolder = "";
    public string LayoutMode = "Zacatecnik"; // "Zacatecnik" nebo "Pokrocily"

    private float _stepDoneTimer;
    private float _autoSaveTimer;
    private float _casAktualnihoKroku;
    private string _lastLoadedUtc = "";
    private float _syncTimer;

    public LessonStep? ActiveStep =>
        Active != null && StepIndex < Active.Kroky.Count ? Active.Kroky[StepIndex] : null;

    // ---------- Načítání a ukládání ----------

    public void LoadStudentSettings()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "student.json");
        if (File.Exists(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("StudentName", out var nameProp))
                    StudentName = nameProp.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("ClassFolder", out var folderProp))
                    ClassFolder = folderProp.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("LayoutMode", out var modeProp))
                    LayoutMode = modeProp.GetString() ?? "Zacatecnik";
            }
            catch { }
        }
    }

    public void SaveStudentSettings(string name, string folder, string layoutMode)
    {
        StudentName = name;
        ClassFolder = folder;
        LayoutMode = layoutMode;

        string path = Path.Combine(AppContext.BaseDirectory, "student.json");
        try
        {
            string json = $"{{\n  \"StudentName\": \"{name.Replace("\"", "\\\"")}\",\n  \"ClassFolder\": \"{folder.Replace("\"", "\\\"")}\",\n  \"LayoutMode\": \"{layoutMode.Replace("\"", "\\\"")}\"\n}}";
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Chyba ukládání student.json: {ex.Message}");
        }
    }

    public void LoadAll(string dir)
    {
        Lessons.Clear();
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var data = JsonSerializer.Deserialize(File.ReadAllText(file), LessonJsonContext.Default.LessonData);
            if (data != null && data.Kroky.Count > 0) Lessons.Add(data);
        }
        Lessons.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); // pořadí kurzu = prefix v Id ("01-", "02-", ...)

        LoadStudentSettings();

        if (File.Exists(ProgressPath))
        {
            Progress = JsonSerializer.Deserialize(File.ReadAllText(ProgressPath), LessonJsonContext.Default.LessonProgress) ?? new LessonProgress();
            _lastLoadedUtc = Progress.PosledniZmenaUtc;
        }
    }

    public void SaveProgress()
    {
        Progress.PosledniZmenaUtc = DateTime.UtcNow.ToString("o");
        _lastLoadedUtc = Progress.PosledniZmenaUtc;
        string json = JsonSerializer.Serialize(Progress, LessonJsonContext.Default.LessonProgress);
        File.WriteAllText(ProgressPath, json);

        // Pokud je nastavená složka třídy a jméno žáka, uložíme kopii tam
        if (!string.IsNullOrEmpty(ClassFolder) && !string.IsNullOrEmpty(StudentName))
        {
            try
            {
                if (!Directory.Exists(ClassFolder))
                {
                    Directory.CreateDirectory(ClassFolder);
                }
                string safeName = MiniEngine.Export.GameExporter.SafeFileName(StudentName);
                string studentPath = Path.Combine(ClassFolder, $"{safeName}.json");
                File.WriteAllText(studentPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Nepodařilo se uložit postup do sdílené složky: {ex.Message}");
            }
        }
    }

    public bool IsCompleted(LessonData l) => Progress.Dokoncene.Contains(l.Id);

    /// <summary>Lekce se odemykají postupně; první je vždy odemčená.</summary>
    public bool IsUnlocked(int index) =>
        OdemknoutVse || index == 0 || (index > 0 && index <= Lessons.Count - 1 && IsCompleted(Lessons[index - 1]));

    // ---------- Běh lekce ----------

    public void StartLesson(LessonData lesson)
    {
        Active = lesson;
        StepIndex = Progress.AktualniLekce == lesson.Id ? Math.Min(Progress.AktualniKrok, lesson.Kroky.Count - 1) : 0;
        State = LessonState.Bezi;
        
        CasLekce = (float)Progress.CelkovyCas;
        PouziteNapovedy = Progress.CelkemNapoved;
        _casAktualnihoKroku = (float)Progress.CasyKroku.ElementAtOrDefault(StepIndex);
        _autoSaveTimer = 0f;

        Progress.AktualniLekce = lesson.Id;
        Progress.AktualniKrok = StepIndex;

        while (Progress.CasyKroku.Count < lesson.Kroky.Count) Progress.CasyKroku.Add(0.0);
        while (Progress.NapovedyKroku.Count < lesson.Kroky.Count) Progress.NapovedyKroku.Add(0);

        SaveProgress();
    }

    public void CloseLesson()
    {
        Active = null;
        State = LessonState.Zadna;
    }

    /// <summary>Volat každý frame z Game.Update (i mimo Play mód – žák tvoří v editoru).</summary>
    public void Update(World world, EditorSelection selection, float dt)
    {
        // Skener externích změn od učitele (např. dálkový skip nebo reset)
        _syncTimer += dt;
        if (_syncTimer >= 3.0f)
        {
            _syncTimer = 0f;
            ReloadProgressIfChanged();
        }

        if (Active == null || State == LessonState.Zadna || State == LessonState.LekceDokoncena) return;

        CasLekce += dt;
        _casAktualnihoKroku += dt;

        // Průběžný autosave (každých 5 sekund)
        _autoSaveTimer += dt;
        if (_autoSaveTimer >= 5.0f)
        {
            _autoSaveTimer = 0f;
            Progress.CelkovyCas = CasLekce;
            if (StepIndex >= 0 && StepIndex < Progress.CasyKroku.Count)
            {
                Progress.CasyKroku[StepIndex] = _casAktualnihoKroku;
            }
            Progress.CelkemNapoved = PouziteNapovedy;
            SaveProgress();
        }

        if (State == LessonState.KrokSplnen)
        {
            _stepDoneTimer -= dt;
            if (_stepDoneTimer <= 0f) NextStep();
            return;
        }

        var step = ActiveStep;
        if (step != null && EvaluateCondition(step.Podminka, world, selection))
        {
            State = LessonState.KrokSplnen;
            _stepDoneTimer = KrokSplnenProdleva;
            ToastSystem.Show($"Krok splněn: {step.Text}");
        }
    }

    /// <summary>Tlačítko "Další krok" – nečekat na prodlevu.</summary>
    public void ConfirmStep()
    {
        if (State == LessonState.KrokSplnen) NextStep();
    }

    /// <summary>Tlačítko "Přeskočit" – posun bez splnění podmínky.</summary>
    public void SkipStep() => NextStep();

    private void NextStep()
    {
        if (Active == null) return;
        
        // Zapsat finální čas a nápovědy pro právě splněný krok
        if (StepIndex >= 0 && StepIndex < Progress.CasyKroku.Count)
        {
            Progress.CasyKroku[StepIndex] = _casAktualnihoKroku;
        }
        _casAktualnihoKroku = 0f;

        StepIndex++;

        if (StepIndex >= Active.Kroky.Count)
        {
            State = LessonState.LekceDokoncena;
            if (!Progress.Dokoncene.Contains(Active.Id)) Progress.Dokoncene.Add(Active.Id);
            Progress.AktualniLekce = "";
            Progress.AktualniKrok = 0;
        }
        else
        {
            State = LessonState.Bezi;
            Progress.AktualniKrok = StepIndex;
            
            while (Progress.CasyKroku.Count <= StepIndex) Progress.CasyKroku.Add(0.0);
            while (Progress.NapovedyKroku.Count <= StepIndex) Progress.NapovedyKroku.Add(0);
        }
        SaveProgress();
    }

    public void ReloadProgressIfChanged()
    {
        if (!string.IsNullOrEmpty(ClassFolder) && !string.IsNullOrEmpty(StudentName))
        {
            try
            {
                string safeName = MiniEngine.Export.GameExporter.SafeFileName(StudentName);
                string studentPath = Path.Combine(ClassFolder, $"{safeName}.json");
                if (File.Exists(studentPath))
                {
                    string content = File.ReadAllText(studentPath);
                    var extProgress = JsonSerializer.Deserialize(content, LessonJsonContext.Default.LessonProgress);
                    if (extProgress != null && extProgress.PosledniZmenaUtc != _lastLoadedUtc)
                    {
                        Progress = extProgress;
                        _lastLoadedUtc = Progress.PosledniZmenaUtc;

                        File.WriteAllText(ProgressPath, content);

                        if (!string.IsNullOrEmpty(Progress.AktualniLekce))
                        {
                            var lesson = Lessons.FirstOrDefault(l => l.Id == Progress.AktualniLekce);
                            if (lesson != null)
                            {
                                Active = lesson;
                                StepIndex = Math.Min(Progress.AktualniKrok, lesson.Kroky.Count - 1);
                                State = StepIndex >= lesson.Kroky.Count ? LessonState.LekceDokoncena : LessonState.Bezi;
                                CasLekce = (float)Progress.CelkovyCas;
                                PouziteNapovedy = Progress.CelkemNapoved;
                                _casAktualnihoKroku = (float)Progress.CasyKroku.ElementAtOrDefault(StepIndex);
                            }
                            else
                            {
                                Active = null;
                                State = LessonState.Zadna;
                            }
                        }
                        else
                        {
                            Active = null;
                            State = LessonState.Zadna;
                        }
                    }
                }
            }
            catch { }
        }
    }

    // ---------- Vyhodnocování podmínek (bez reflexe) ----------

    public int FindEntity(World world, string jmeno)
    {
        var names = world.Store<Name>();
        var ents = names.Entities;
        var span = names.Span;
        for (int i = 0; i < span.Length; i++)
            if (span[i].Value == jmeno) return ents[i];
        return -1;
    }

    private bool EvaluateCondition(LessonCondition c, World world, EditorSelection selection)
    {
        switch (c.Typ)
        {
            case "VybranaEntita":
            {
                if (!selection.HasSelection) return false;
                var names = world.Store<Name>();
                return names.Has(selection.EntityIndex) && names.Get(selection.EntityIndex).Value == c.Jmeno;
            }
            case "MaKomponentu":
            {
                int idx = FindEntity(world, c.Jmeno);
                if (idx < 0) return false;
                return c.Komponenta switch
                {
                    "Light" => world.Store<LightComponent>().Has(idx),
                    "ParticleEmitter" => world.Store<ParticleEmitter>().Has(idx),
                    "AudioSource" => world.Store<AudioSourceComponent>().Has(idx),
                    "Behavior" => world.Store<BehaviorComponent>().Has(idx),
                    "RigidBody" => world.Store<RigidBodyRef>().Has(idx),
                    _ => false // TODO: doplň další komponenty podle potřeby lekcí
                };
            }
            case "Vlastnost":
            {
                int idx = FindEntity(world, c.Jmeno);
                return idx >= 0 && ReadProperty(world, idx, c.Cesta, out float v) && v >= c.Min && v <= c.Max;
            }
            case "VzdalenostOd":
            {
                int a = FindEntity(world, c.Jmeno);
                int b = FindEntity(world, c.Od);
                var transforms = world.Store<Transform>();
                if (a < 0 || b < 0 || !transforms.Has(a) || !transforms.Has(b)) return false;
                var pa = transforms.Get(a).World.Translation;
                var pb = transforms.Get(b).World.Translation;
                return Vector3.Distance(pa, pb) <= c.Vzdalenost;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Čtení číselné vlastnosti podle cesty. Explicitní switch = AOT-safe.
    /// Rozšiřuj podle toho, co lekce potřebují kontrolovat.
    /// </summary>
    private static bool ReadProperty(World world, int idx, string cesta, out float value)
    {
        value = 0f;
        switch (cesta)
        {
            case "Light.Intensity":
                if (!world.Store<LightComponent>().Has(idx)) return false;
                value = world.Store<LightComponent>().Get(idx).Intensity; return true;
            case "Light.Radius":
                if (!world.Store<LightComponent>().Has(idx)) return false;
                value = world.Store<LightComponent>().Get(idx).Radius; return true;
            case "Light.Color.R":
                if (!world.Store<LightComponent>().Has(idx)) return false;
                value = world.Store<LightComponent>().Get(idx).Color.X; return true;
            case "Light.Color.B":
                if (!world.Store<LightComponent>().Has(idx)) return false;
                value = world.Store<LightComponent>().Get(idx).Color.Z; return true;
            case "Transform.Position.Y":
                if (!world.Store<Transform>().Has(idx)) return false;
                value = world.Store<Transform>().Get(idx).Position.Y; return true;
            case "MeshRenderer.Tint.R":
                if (!world.Store<MeshRenderer>().Has(idx)) return false;
                value = world.Store<MeshRenderer>().Get(idx).Tint.X; return true;
            case "Behavior.Speed":
                if (!world.Store<BehaviorComponent>().Has(idx)) return false;
                value = world.Store<BehaviorComponent>().Get(idx).Speed; return true;
            default:
                return false; // TODO: doplň další cesty
        }
    }
}
