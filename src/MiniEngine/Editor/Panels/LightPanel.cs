using System.Numerics;
using ImGuiNET;
using MiniEngine.Rendering;

namespace MiniEngine.Editor;

/// <summary>
/// Nastaveni slunce a okolniho svetla. Smer se zadava lidsky - otoceni po obloze
/// (azimut) a vyska nad obzorem - misto XYZ vektoru.
/// </summary>
public sealed class LightPanel
{
    private readonly LightingShader _lighting;
    private readonly PostProcessing _postProcessing;

    private const float DefaultAzimuth = 35f;
    private const float DefaultElevation = 57f;

    private float _azimuthDeg = DefaultAzimuth;
    private float _elevationDeg = DefaultElevation;

    public Action? OnChanged;
    public MiniEngine.Lessons.LessonSystem? LessonSystem { get; set; }

    public LightPanel(LightingShader lighting, PostProcessing postProcessing)
    {
        _lighting = lighting;
        _postProcessing = postProcessing;
        UpdateDirection();
    }

    public void LoadFrom(Vector3 dir)
    {
        float el = MathF.Asin(Math.Clamp(-dir.Y, -1f, 1f));
        float az = MathF.Atan2(-dir.Z, -dir.X);
        if (az < 0) az += 2f * MathF.PI;

        _azimuthDeg = az * 180f / MathF.PI;
        _elevationDeg = el * 180f / MathF.PI;
    }

    public void Draw()
    {
        ImGui.Begin("Svetlo & Efekty");
        DrawInner();
        ImGui.End();
    }

    public void DrawInner()
    {
        bool isBeginner = LessonSystem != null && LessonSystem.LayoutMode == "Zacatecnik";

        ImGui.PushItemWidth(-110f);

        bool changed = false;

        if (ImGui.SliderFloat("Otoceni", ref _azimuthDeg, 0f, 360f, "%.0f st"))
        {
            UpdateDirection();
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

        if (ImGui.SliderFloat("Vyska slunce", ref _elevationDeg, 5f, 90f, "%.0f st"))
        {
            UpdateDirection();
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

        ImGui.SliderFloat("Sila slunce", ref _lighting.SunIntensity, 0f, 2f, "%.2f");
        if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

        ImGui.ColorEdit3("Barva slunce", ref _lighting.SunColor);
        if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

        ImGui.ColorEdit3("Okolni svetlo", ref _lighting.Ambient);
        if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

        ImGui.SliderFloat("Odlesky", ref _lighting.SpecStrength, 0f, 1f, "%.2f");
        if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

        if (!isBeginner)
        {
            ImGui.Separator();
            ImGui.Text("Post-processing (Efekty)");

            if (ImGui.Checkbox("Aktivovat efekty", ref _postProcessing.Enabled)) changed = true;

            if (_postProcessing.Enabled)
            {
                ImGui.SliderFloat("Intenzita Bloom", ref _postProcessing.BloomIntensity, 0f, 2f, "%.2f");
                if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

                ImGui.SliderFloat("Jasovy prah Bloom", ref _postProcessing.BloomThreshold, 0.1f, 1.0f, "%.2f");
                if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

                ImGui.SliderFloat("Sila Vignette", ref _postProcessing.VignettePower, 0f, 1f, "%.2f");
                if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

                ImGui.SliderFloat("Saturace barev", ref _postProcessing.Saturation, 0f, 2f, "%.2f");
                if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

                ImGui.SliderFloat("Kontrast obrazu", ref _postProcessing.Contrast, 0.5f, 2.0f, "%.2f");
                if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

                ImGui.SliderFloat("Chromatická aberace", ref _postProcessing.ChromaticAberration, 0.0f, 0.08f, "%.3f");
                if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("Vychozi"))
        {
            _azimuthDeg = DefaultAzimuth;
            _elevationDeg = DefaultElevation;
            _lighting.SunIntensity = LightingShader.DefaultSunIntensity;
            _lighting.SunColor = LightingShader.DefaultSunColor;
            _lighting.Ambient = LightingShader.DefaultAmbient;
            _lighting.SpecStrength = LightingShader.DefaultSpecStrength;
            
            _postProcessing.Enabled = true;
            _postProcessing.BloomIntensity = 0.6f;
            _postProcessing.BloomThreshold = 0.7f;
            _postProcessing.VignettePower = 0.3f;
            _postProcessing.Saturation = 1.0f;
            _postProcessing.Contrast = 1.0f;
            _postProcessing.ChromaticAberration = 0.0f;
            
            UpdateDirection();
            changed = true;
        }

        if (changed)
        {
            OnChanged?.Invoke();
        }

        ImGui.PopItemWidth();
    }

    /// <summary>Azimut + vyska -> normalizovany smer OD slunce ke scene.</summary>
    private void UpdateDirection()
    {
        float az = _azimuthDeg * MathF.PI / 180f;
        float el = _elevationDeg * MathF.PI / 180f;

        _lighting.SunDirection = new Vector3(
            -MathF.Cos(el) * MathF.Cos(az),
            -MathF.Sin(el),
            -MathF.Cos(el) * MathF.Sin(az));
    }
}
