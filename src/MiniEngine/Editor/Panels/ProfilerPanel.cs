using System;
using System.Diagnostics;
using ImGuiNET;

namespace MiniEngine.Editor;

public sealed class ProfilerPanel
{
    private const int HistorySize = 100;
    
    private readonly float[] _fpsHistory = new float[HistorySize];
    private readonly float[] _msHistory = new float[HistorySize];
    private readonly float[] _ramHistory = new float[HistorySize];
    private readonly float[] _particleHistory = new float[HistorySize];

    public void AddFrameData(float fps, float ms, float ramBytes, int particleCount)
    {
        // Posun historie doleva
        Array.Copy(_fpsHistory, 1, _fpsHistory, 0, HistorySize - 1);
        Array.Copy(_msHistory, 1, _msHistory, 0, HistorySize - 1);
        Array.Copy(_ramHistory, 1, _ramHistory, 0, HistorySize - 1);
        Array.Copy(_particleHistory, 1, _particleHistory, 0, HistorySize - 1);

        // Zápis nových hodnot na konec
        _fpsHistory[HistorySize - 1] = fps;
        _msHistory[HistorySize - 1] = ms;
        _ramHistory[HistorySize - 1] = ramBytes / 1024f; // v KB
        _particleHistory[HistorySize - 1] = particleCount;
    }

    public void Draw()
    {
        ImGui.Begin("Profiler (Výkon)");

        // 1. Sekce: FPS a Čas snímku
        float currentFps = _fpsHistory[HistorySize - 1];
        float currentMs = _msHistory[HistorySize - 1];

        float maxFps = GetMax(_fpsHistory, 60f);
        ImGui.Text($"FPS: {currentFps:0.0} (Cíl: 60 FPS)");
        ImGui.PlotLines("##FPS", ref _fpsHistory[0], HistorySize, 0, "", 0f, maxFps * 1.1f, new System.Numerics.Vector2(-1, 50));

        float maxMs = GetMax(_msHistory, 16.6f);
        ImGui.Text($"Čas snímku (Frame Time): {currentMs:0.00} ms");
        ImGui.PlotLines("##MS", ref _msHistory[0], HistorySize, 0, "", 0f, maxMs * 1.1f, new System.Numerics.Vector2(-1, 50));

        ImGui.Separator();

        // 2. Sekce: Paměť a alokace
        float currentRamKb = _ramHistory[HistorySize - 1];
        float maxRam = GetMax(_ramHistory, 10f);
        ImGui.Text($"CPU alokace / snímek: {currentRamKb:0.0} KB");
        ImGui.PlotLines("##RAM", ref _ramHistory[0], HistorySize, 0, "", 0f, maxRam * 1.1f, new System.Numerics.Vector2(-1, 50));

        ImGui.Separator();

        // 3. Sekce: Herní a grafické statistiky
        int currentParticles = (int)_particleHistory[HistorySize - 1];
        float maxParticles = GetMax(_particleHistory, 100f);
        ImGui.Text($"Aktivní částice: {currentParticles}");
        ImGui.PlotLines("##Particles", ref _particleHistory[0], HistorySize, 0, "", 0f, maxParticles * 1.1f, new System.Numerics.Vector2(-1, 50));

        ImGui.End();
    }

    private static float GetMax(float[] history, float defaultMax)
    {
        float max = defaultMax;
        for (int i = 0; i < history.Length; i++)
        {
            if (history[i] > max)
                max = history[i];
        }
        return max;
    }
}
