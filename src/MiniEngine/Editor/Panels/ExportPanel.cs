using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using ImGuiNET;
using MiniEngine.Core;
using MiniEngine.Assets;
using MiniEngine.Export;

namespace MiniEngine.Editor.Panels;

public sealed class ExportPanel
{
    private static readonly Vector4 Gold = new(0.85f, 0.70f, 0.25f, 1f);
    private static readonly Vector4 GoldBg = new(0.85f, 0.70f, 0.25f, 0.16f);
    private static readonly Vector4 Green = new(0.35f, 0.70f, 0.41f, 1f);
    private static readonly Vector4 Red = new(0.88f, 0.32f, 0.32f, 1f);
    private static readonly Vector4 Dim = new(0.60f, 0.63f, 0.70f, 1f);
    private static readonly Vector4 PanelBg = new(0.06f, 0.07f, 0.09f, 0.95f);

    private readonly AssetManager _assets;
    private readonly GameExporter _exporter;

    public bool Open;

    // Form fields
    private string _gameName = "Moje hra";
    private Vector3 _iconColor = new(0.85f, 0.70f, 0.25f);
    private int _platformIndex = OperatingSystem.IsWindows() ? 0 : 1; // 0 = Windows, 1 = macOS
    private bool _fullscreen = true;
    private string _selectedPlayerEntity = "";
    private string _outputPath = "";

    // Stats
    private bool _statsCalculated;
    private int _assetCount;
    private double _assetSizeMb;
    private double _playerSizeMb;

    // Export progress state
    private bool _exporting;
    private float _progressValue;
    private string _progressText = "";
    private readonly List<(string text, string status)> _steps = new(); // status: "pending", "running", "success", "failed"
    private bool _exportFinished;
    private bool _exportSuccess;
    private string _errorMessage = "";
    private string _outputExePath = "";

    public ExportPanel(AssetManager assets)
    {
        _assets = assets;
        _exporter = new GameExporter(assets);
        // Pre-initialize output path to desktop/MojeHry/moje-hra
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _outputPath = Path.Combine(desktop, "MojeHry", "moje-hra");
    }

    public void Draw(World world, string sceneJsonPath, string skyboxTexturePath)
    {
        if (!Open) return;

        var viewportSize = new Vector2(Raylib_cs.Raylib.GetScreenWidth(), Raylib_cs.Raylib.GetScreenHeight());
        ImGui.SetNextWindowPos(viewportSize * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(480f, 0f), ImGuiCond.Appearing);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, Gold);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 16f));

        if (ImGui.Begin("Exportovat mou hru", ref Open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse))
        {
            if (!_exporting && !_exportFinished)
            {
                DrawConfig(world, sceneJsonPath, skyboxTexturePath);
            }
            else
            {
                DrawProgress();
            }
        }
        ImGui.End();

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }

    private void CalculateStats(World world)
    {
        try
        {
            var tempSettings = new GameSettings { Nazev = _gameName };
            var usedAssets = _exporter.CollectUsedAssets(world, tempSettings);
            _assetCount = usedAssets.Count;
            long totalBytes = 0;
            string assetsSrc = Path.Combine(AppContext.BaseDirectory, "assets");

            foreach (var asset in usedAssets)
            {
                string src = Path.Combine(assetsSrc, asset);
                if (File.Exists(src))
                {
                    totalBytes += new FileInfo(src).Length;
                }
            }
            _assetSizeMb = totalBytes / (1024.0 * 1024.0);

            // Estimate player size
            string platform = _platformIndex == 0 ? "win-x64" : "osx-arm64";
            string playerSrc = Path.Combine(AppContext.BaseDirectory, "player", platform,
                platform == "win-x64" ? "MiniPlayer.exe" : "MiniPlayer");
            if (File.Exists(playerSrc))
            {
                _playerSizeMb = new FileInfo(playerSrc).Length / (1024.0 * 1024.0);
            }
            else
            {
                _playerSizeMb = 14.0; // default estimate
            }
            _statsCalculated = true;
        }
        catch
        {
            _statsCalculated = false;
        }
    }

    private void DrawConfig(World world, string sceneJsonPath, string skyboxTexturePath)
    {
        if (!_statsCalculated)
        {
            CalculateStats(world);
        }

        // Title text
        ImGui.TextColored(Gold, "EXPORT HRY (BEZ KOMPILACE)");
        ImGui.Separator();
        ImGui.Spacing();

        // 1. Název hry
        ImGui.Text("NÁZEV HRY:");
        if (ImGui.InputText("##GameName", ref _gameName, 64))
        {
            string safeName = GameExporter.SafeFileName(_gameName);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            _outputPath = Path.Combine(desktop, "MojeHry", safeName);
            _statsCalculated = false;
        }

        ImGui.Spacing();

        // 2. Barva ikony
        ImGui.Text("BARVA IKONY / SPLASH OBRAZOVKY:");
        ImGui.ColorEdit3("##IconColor", ref _iconColor);

        ImGui.Spacing();

        // 3. Platforma
        ImGui.Text("CÍLOVÁ PLATFORMA:");
        string[] platforms = { "Windows (win-x64)", "macOS (osx-arm64)" };
        if (ImGui.Combo("##Platform", ref _platformIndex, platforms, platforms.Length))
        {
            _statsCalculated = false;
        }

        ImGui.Spacing();

        // 4. Možnosti
        ImGui.Text("MOŽNOSTI:");
        ImGui.Checkbox("Spustit na celou obrazovku", ref _fullscreen);

        ImGui.Spacing();

        // 5. Hráč
        ImGui.Text("ENTITA HRÁČE (Character Controller):");
        
        // Find entities in the world
        var transforms = world.Store<Transform>();
        var names = world.Store<Name>();
        var entities = transforms.Entities;
        List<string> playerOptions = new() { "Volná kamera (bez hráče)" };
        for (int i = 0; i < entities.Length; i++)
        {
            int idx = entities[i];
            string entName = names.Has(idx) ? names.Get(idx).Value : $"Entity {idx}";
            if (!string.IsNullOrEmpty(entName)) playerOptions.Add(entName);
        }

        // Find current selection index
        int selectedIndex = 0;
        if (!string.IsNullOrEmpty(_selectedPlayerEntity))
        {
            selectedIndex = playerOptions.IndexOf(_selectedPlayerEntity);
            if (selectedIndex < 0) selectedIndex = 0;
        }

        string currentSelectedLabel = playerOptions[selectedIndex];
        if (ImGui.BeginCombo("##PlayerEntity", currentSelectedLabel))
        {
            for (int i = 0; i < playerOptions.Count; i++)
            {
                bool isSelected = (i == selectedIndex);
                if (ImGui.Selectable(playerOptions[i], isSelected))
                {
                    _selectedPlayerEntity = (i == 0) ? "" : playerOptions[i];
                }
                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();

        // 6. Výstupní složka
        ImGui.Text("CÍLOVÁ SLOŽKA PRO EXPORT:");
        ImGui.InputText("##OutputDir", ref _outputPath, 256);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Stats summary
        ImGui.TextColored(Dim, "Přibalí se:");
        ImGui.TextColored(Dim, " • scéna (scene.json)");
        ImGui.TextColored(Dim, $" • {_assetCount} použitých assetů ({_assetSizeMb:F1} MB)");
        ImGui.TextColored(Dim, $" • přehrávač MiniPlayer ({_playerSizeMb:F1} MB)");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Action Buttons
        float availW = ImGui.GetContentRegionAvail().X;
        float btnW = (availW - ImGui.GetStyle().ItemSpacing.X) * 0.5f;

        ImGui.PushStyleColor(ImGuiCol.Button, Gold);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.09f, 0.11f, 1f));
        if (ImGui.Button("Exportovat", new Vector2(btnW, 0f)))
        {
            StartExport(world, sceneJsonPath, skyboxTexturePath);
        }
        ImGui.PopStyleColor(2);

        ImGui.SameLine();
        if (ImGui.Button("Zrušit", new Vector2(btnW, 0f)))
        {
            Open = false;
        }
    }

    private void DrawProgress()
    {
        if (_exporting)
        {
            ImGui.TextColored(Gold, $"Exportuji „{_gameName}“...");
        }
        else if (_exportFinished && _exportSuccess)
        {
            ImGui.TextColored(Green, "✓ Hra je hotová!");
        }
        else
        {
            ImGui.TextColored(Red, "✕ Export selhal!");
        }

        ImGui.Separator();
        ImGui.Spacing();

        // Draw steps
        for (int i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            Vector4 color = Dim;
            string prefix = "[ ]";

            if (step.status == "success")
            {
                color = Green;
                prefix = "✓";
            }
            else if (step.status == "running")
            {
                color = Gold;
                prefix = "▸";
            }
            else if (step.status == "failed")
            {
                color = Red;
                prefix = "✕";
            }

            ImGui.TextColored(color, $"{prefix} {step.text}");
        }

        ImGui.Spacing();
        
        // Progress bar
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Gold);
        ImGui.ProgressBar(_progressValue, new Vector2(-1f, 8f), _progressText);
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (_exportFinished)
        {
            if (_exportSuccess)
            {
                ImGui.TextWrapped("Zkopíruj složku na flashku nebo pošli — hra běží bez instalace.");
                ImGui.Spacing();

                float availW = ImGui.GetContentRegionAvail().X;
                float btnW = (availW - ImGui.GetStyle().ItemSpacing.X * 2) / 3f;

                ImGui.PushStyleColor(ImGuiCol.Button, Gold);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.09f, 0.11f, 1f));
                if (ImGui.Button("Spustit hru", new Vector2(btnW, 0f)))
                {
                    RunGame();
                }
                ImGui.PopStyleColor(2);

                ImGui.SameLine();
                if (ImGui.Button("Otevřít složku", new Vector2(btnW, 0f)))
                {
                    OpenOutputFolder();
                }

                ImGui.SameLine();
                if (ImGui.Button("Zavřít", new Vector2(btnW, 0f)))
                {
                    Open = false;
                    _exportFinished = false;
                    _statsCalculated = false;
                }
            }
            else
            {
                ImGui.TextColored(Red, "Chyba:");
                ImGui.TextWrapped(_errorMessage);
                ImGui.Spacing();

                float availW = ImGui.GetContentRegionAvail().X;
                float btnW = (availW - ImGui.GetStyle().ItemSpacing.X) * 0.5f;

                ImGui.PushStyleColor(ImGuiCol.Button, Gold);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.09f, 0.11f, 1f));
                if (ImGui.Button("Zkusit znovu", new Vector2(btnW, 0f)))
                {
                    _exportFinished = false;
                    _statsCalculated = false;
                }
                ImGui.PopStyleColor(2);

                ImGui.SameLine();
                if (ImGui.Button("Zavřít", new Vector2(btnW, 0f)))
                {
                    Open = false;
                    _exportFinished = false;
                    _statsCalculated = false;
                }
            }
        }
    }

    private void StartExport(World world, string sceneJsonPath, string skyboxTexturePath)
    {
        _exporting = true;
        _exportFinished = false;
        _exportSuccess = false;
        _errorMessage = "";
        _progressValue = 0f;
        _progressText = "Příprava...";

        _steps.Clear();
        _steps.Add(("Kopíruji přehrávač", "pending"));
        _steps.Add(("Ukládám scénu a game.json", "pending"));
        _steps.Add(("Balím assety", "pending"));
        _steps.Add(("Kontrola spuštění", "pending"));

        var opt = new ExportOptions
        {
            Hra = new GameSettings
            {
                Nazev = _gameName,
                Fullscreen = _fullscreen,
                HracEntity = _selectedPlayerEntity,
                BarvaIkony = $"#{(int)(_iconColor.X * 255):X2}{(int)(_iconColor.Y * 255):X2}{(int)(_iconColor.Z * 255):X2}",
                Skybox = skyboxTexturePath
            },
            Platforma = _platformIndex == 0 ? "win-x64" : "osx-arm64",
            VystupniSlozka = _outputPath
        };

        // Run async
        Task.Run(() =>
        {
            try
            {
                _exporter.OnProgress = (krok, podil) =>
                {
                    _progressValue = podil;
                    _progressText = krok;

                    if (podil < 0.20f)
                    {
                        _steps[0] = ("Kopíruji přehrávač", "running");
                    }
                    else if (podil >= 0.20f && podil < 0.25f)
                    {
                        _steps[0] = ("Kopíruji přehrávač", "success");
                        _steps[1] = ("Ukládám scénu a game.json", "running");
                    }
                    else if (podil >= 0.25f && podil < 0.95f)
                    {
                        _steps[0] = ("Kopíruji přehrávač", "success");
                        _steps[1] = ("Ukládám scénu a game.json", "success");
                        _steps[2] = (krok, "running"); // Balím assety X/Y
                    }
                    else if (podil >= 0.95f && podil < 1.0f)
                    {
                        _steps[0] = ("Kopíruji přehrávač", "success");
                        _steps[1] = ("Ukládám scénu a game.json", "success");
                        _steps[2] = ("Balím assety", "success");
                        _steps[3] = ("Kontrola spuštění", "running");
                    }
                };

                _outputExePath = _exporter.Export(world, opt, sceneJsonPath);

                _steps[0] = ("Kopíruji přehrávač", "success");
                _steps[1] = ("Ukládám scénu a game.json", "success");
                _steps[2] = ("Balím assety", "success");
                _steps[3] = ("Kontrola spuštění", "success");

                _progressValue = 1.0f;
                _exportSuccess = true;
            }
            catch (Exception ex)
            {
                _errorMessage = ex.Message;
                _exportSuccess = false;

                for (int i = 0; i < _steps.Count; i++)
                {
                    if (_steps[i].status == "running" || _steps[i].status == "pending")
                    {
                        _steps[i] = (_steps[i].text, "failed");
                        break;
                    }
                }
            }
            finally
            {
                _exportFinished = true;
                _exporting = false;
            }
        });
    }

    private void RunGame()
    {
        if (string.IsNullOrEmpty(_outputExePath) || !File.Exists(_outputExePath)) return;
        try
        {
            var psi = new ProcessStartInfo(_outputExePath)
            {
                WorkingDirectory = Path.GetDirectoryName(_outputExePath)!,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Chyba spuštění hry: {ex.Message}");
        }
    }

    private void OpenOutputFolder()
    {
        if (string.IsNullOrEmpty(_outputPath) || !Directory.Exists(_outputPath)) return;
        try
        {
            var psi = new ProcessStartInfo(_outputPath)
            {
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Chyba otevření složky: {ex.Message}");
        }
    }
}
