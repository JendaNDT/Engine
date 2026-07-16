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

    private const float DefaultAzimuth = 35f;
    private const float DefaultElevation = 57f;

    private float _azimuthDeg = DefaultAzimuth;
    private float _elevationDeg = DefaultElevation;

    public LightPanel(LightingShader lighting)
    {
        _lighting = lighting;
        UpdateDirection();
    }

    public void Draw()
    {
        ImGui.Begin("Svetlo");
        ImGui.PushItemWidth(-110f);

        bool dirChanged = false;
        dirChanged |= ImGui.SliderFloat("Otoceni", ref _azimuthDeg, 0f, 360f, "%.0f st");
        dirChanged |= ImGui.SliderFloat("Vyska slunce", ref _elevationDeg, 5f, 90f, "%.0f st");
        if (dirChanged)
            UpdateDirection();

        ImGui.SliderFloat("Sila slunce", ref _lighting.SunIntensity, 0f, 2f, "%.2f");
        ImGui.ColorEdit3("Barva slunce", ref _lighting.SunColor);
        ImGui.ColorEdit3("Okolni svetlo", ref _lighting.Ambient);
        ImGui.SliderFloat("Odlesky", ref _lighting.SpecStrength, 0f, 1f, "%.2f");

        ImGui.Spacing();
        if (ImGui.Button("Vychozi"))
        {
            _azimuthDeg = DefaultAzimuth;
            _elevationDeg = DefaultElevation;
            _lighting.SunIntensity = LightingShader.DefaultSunIntensity;
            _lighting.SunColor = LightingShader.DefaultSunColor;
            _lighting.Ambient = LightingShader.DefaultAmbient;
            _lighting.SpecStrength = LightingShader.DefaultSpecStrength;
            UpdateDirection();
        }

        ImGui.PopItemWidth();
        ImGui.End();
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
