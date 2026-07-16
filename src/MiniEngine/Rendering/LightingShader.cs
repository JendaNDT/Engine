using System.Numerics;
using Raylib_cs;

namespace MiniEngine.Rendering;

/// <summary>
/// Sdileny osvetlovaci shader: jedno smerove slunce + ambient + Blinn-Phong odlesk.
/// Standardni raylib uniformy (mvp, matModel, matNormal, colDiffuse, texture0)
/// si raylib binduje sam, vlastni (viewPos, sunDir, ...) se cachuji tady -
/// GetShaderLocation je string lookup, nepatri do smycky.
/// </summary>
public sealed class LightingShader : IDisposable
{
    public Shader Shader;

    private readonly int _locViewPos;
    private readonly int _locSunDir;
    private readonly int _locSunColor;
    private readonly int _locAmbient;
    private readonly int _locSpec;

    // Vychozi hodnoty ztlumene, at sceny nejsou prepalene (puvodni donut svitil do zluta).
    public Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.35f));
    public Vector3 SunColor = new(1.00f, 0.95f, 0.88f);
    public float SunIntensity = 0.85f;
    public Vector3 Ambient = new(0.24f, 0.26f, 0.30f);
    public float SpecStrength = 0.25f;

    public static readonly Vector3 DefaultSunColor = new(1.00f, 0.95f, 0.88f);
    public static readonly Vector3 DefaultAmbient = new(0.24f, 0.26f, 0.30f);
    public const float DefaultSunIntensity = 0.85f;
    public const float DefaultSpecStrength = 0.25f;

    public LightingShader()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        Shader = Raylib.LoadShader(
            Path.Combine(dir, "lighting.vs"),
            Path.Combine(dir, "lighting.fs"));

        _locViewPos = Raylib.GetShaderLocation(Shader, "viewPos");
        _locSunDir = Raylib.GetShaderLocation(Shader, "sunDir");
        _locSunColor = Raylib.GetShaderLocation(Shader, "sunColor");
        _locAmbient = Raylib.GetShaderLocation(Shader, "ambientColor");
        _locSpec = Raylib.GetShaderLocation(Shader, "specStrength");
    }

    /// <summary>Jednou per frame pred kreslenim sceny.</summary>
    public void Apply(Camera3D camera)
    {
        Raylib.SetShaderValue(Shader, _locViewPos, camera.Position, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locSunDir, SunDirection, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locSunColor, SunColor * SunIntensity, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locAmbient, Ambient, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locSpec, SpecStrength, ShaderUniformDataType.Float);
    }

    public void Dispose() => Raylib.UnloadShader(Shader);
}
