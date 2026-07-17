using System.Text.Json;
using System.Text.Json.Serialization;
using MiniEngine.Core;
using MiniEngine.Assets;

namespace MiniEngine.Export;

// ============================================================
// Fáze 39 – „Exportuj mou hru": bez kompilace na žákovském PC.
// Export = zkopírovat předkompilovaný MiniPlayer + game.json
// + scene.json + JEN použité assety. Vteřiny, offline.
//
// Očekávaná struktura vedle editoru (plní CI build.yml):
//   player/win-x64/MiniPlayer.exe
//   player/osx-arm64/MiniPlayer
//
// Volání z Game.cs (dialog 4a/4b):
//   var exporter = new GameExporter(_assets);
//   exporter.OnProgress = (krok, podil) => { ...update dialogu... };
//   exporter.Export(world, options, cestaKeSceneJson);
// ============================================================

public sealed class GameSettings
{
    public string Nazev { get; set; } = "Moje hra";
    public bool Fullscreen { get; set; } = true;
    /// <summary>Jméno entity hráče (character controller). Prázdné = volná kamera.</summary>
    public string HracEntity { get; set; } = "";
    /// <summary>Barva ikony/splash (hex), vybraná v dialogu.</summary>
    public string BarvaIkony { get; set; } = "#D9B23F";
    public string Skybox { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GameSettings))]
internal partial class GameJsonContext : JsonSerializerContext { }

public sealed class ExportOptions
{
    public GameSettings Hra = new();
    /// <summary>"win-x64" nebo "osx-arm64". Výchozí = aktuální OS.</summary>
    public string Platforma = OperatingSystem.IsWindows() ? "win-x64" : "osx-arm64";
    /// <summary>Cílová složka, např. ~/Plocha/MojeHry/&lt;název&gt;.</summary>
    public string VystupniSlozka = "";
}

public sealed class GameExporter
{
    private readonly AssetManager _assets;

    /// <summary>(popis kroku, podíl 0..1) – pro progress v dialogu 4b.</summary>
    public Action<string, float>? OnProgress;

    public GameExporter(AssetManager assets) => _assets = assets;

    /// <summary>
    /// Projde ECS a posbírá relativní cesty všech použitých assetů.
    /// Modely přes AssetManager.GetRelativePath(handle), textury/zvuky
    /// jsou v komponentách jako stringy.
    /// </summary>
    public List<string> CollectUsedAssets(World world, GameSettings hra)
    {
        var set = new HashSet<string>();

        var renderers = world.Store<MeshRenderer>().Span;
        for (int i = 0; i < renderers.Length; i++)
        {
            ref var r = ref renderers[i];
            AddIfSet(set, _assets.GetRelativePath(r.ModelHandle));
            AddIfSet(set, r.AlbedoTexturePath);
            AddIfSet(set, r.NormalMapPath);
            AddIfSet(set, r.MetallicRoughnessMapPath);
        }

        var emitters = world.Store<ParticleEmitter>().Span;
        for (int i = 0; i < emitters.Length; i++)
            AddIfSet(set, emitters[i].TexturePath);

        var sounds = world.Store<AudioSourceComponent>().Span;
        for (int i = 0; i < sounds.Length; i++)
            AddIfSet(set, sounds[i].ClipPath);

        AddIfSet(set, hra.Skybox);

        // Shadery a font potřebuje přehrávač vždy
        set.Add("shaders/lighting.vs"); set.Add("shaders/lighting.fs");
        set.Add("shaders/skybox.vs"); set.Add("shaders/skybox.fs");
        set.Add("shaders/postprocess.fs");
        set.Add("fonts/Roboto-Regular.ttf");

        var list = new List<string>(set);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static void AddIfSet(HashSet<string> set, string? path)
    {
        if (!string.IsNullOrEmpty(path)) set.Add(path);
    }

    /// <summary>
    /// Provede export. sceneJsonPath = aktuální uložená scéna
    /// (Game.cs před exportem zavolá SceneSerializer.Save).
    /// Vrací cestu ke spustitelnému souboru, nebo vyhodí výjimku
    /// (dialog ji ukáže červeně u aktivního kroku + "Zkusit znovu").
    /// </summary>
    public string Export(World world, ExportOptions opt, string sceneJsonPath)
    {
        string exeName = SafeFileName(opt.Hra.Nazev);
        string outDir = opt.VystupniSlozka;
        // Stejná struktura jako u editoru (assets/ vedle exe) — AssetManager,
        // LightingShader i PostProcessing pak fungují beze změn.
        string assetsSrc = Path.Combine(AppContext.BaseDirectory, "assets");

        // 1/4 – přehrávač
        OnProgress?.Invoke("Kopíruji přehrávač", 0.05f);
        string playerSrc = Path.Combine(AppContext.BaseDirectory, "player", opt.Platforma,
            opt.Platforma == "win-x64" ? "MiniPlayer.exe" : "MiniPlayer");
        if (!File.Exists(playerSrc))
            throw new FileNotFoundException($"Přehrávač pro {opt.Platforma} chybí: {playerSrc}");

        Directory.CreateDirectory(outDir);
        string exeDst = Path.Combine(outDir, opt.Platforma == "win-x64" ? exeName + ".exe" : exeName);
        File.Copy(playerSrc, exeDst, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(exeDst, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                         UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                         UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        // Zkopírovat doprovodné nativní knihovny (.dll, .dylib, .so) ze složky přehrávače
        string playerDir = Path.GetDirectoryName(playerSrc)!;
        if (Directory.Exists(playerDir))
        {
            foreach (var file in Directory.GetFiles(playerDir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".dll" || ext == ".dylib" || ext == ".so")
                {
                    string dst = Path.Combine(outDir, Path.GetFileName(file));
                    File.Copy(file, dst, overwrite: true);
                }
            }
        }

        // 2/4 – scéna + nastavení
        OnProgress?.Invoke("Ukládám scénu a game.json", 0.20f);
        File.Copy(sceneJsonPath, Path.Combine(outDir, "scene.json"), overwrite: true);
        File.WriteAllText(Path.Combine(outDir, "game.json"),
            JsonSerializer.Serialize(opt.Hra, GameJsonContext.Default.GameSettings));

        // 3/4 – assety (jen použité)
        var used = CollectUsedAssets(world, opt.Hra);
        for (int i = 0; i < used.Count; i++)
        {
            OnProgress?.Invoke($"Balím assety ({i + 1}/{used.Count})", 0.25f + 0.65f * (i + 1) / used.Count);
            string src = Path.Combine(assetsSrc, used[i]);
            if (!File.Exists(src)) continue; // chybějící asset nezastaví export, jen se přeskočí
            string dst = Path.Combine(outDir, "assets", used[i]);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }

        // 4/4 – kontrola
        OnProgress?.Invoke("Kontrola spuštění", 0.95f);
        if (!File.Exists(Path.Combine(outDir, "scene.json")))
            throw new InvalidOperationException("Export se nepovedl: chybí scene.json.");

        OnProgress?.Invoke("Hotovo", 1f);
        return exeDst;
    }

    /// <summary>„Donutová honička" → „Donutova-honicka" (bez diakritiky a mezer).</summary>
    public static string SafeFileName(string nazev)
    {
        var norm = nazev.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(norm.Length);
        foreach (char c in norm)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        }
        var s = sb.ToString().Trim('-');
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.Length == 0 ? "MojeHra" : s;
    }
}
