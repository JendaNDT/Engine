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

    private int _locViewPos;
    private int _locSunDir;
    private int _locSunColor;
    private int _locAmbient;
    private int _locSpec;

    // Shadow Mapping
    public uint ShadowFboId;
    public Texture2D ShadowMapTexture;
    public Matrix4x4 LightMvp;
    private int _locMvpLight;
    private int _locShadowMap;

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

        // Inicializace stinové mapy
        ShadowMapTexture = new Texture2D
        {
            Id = Rlgl.LoadTextureDepth(2048, 2048, false),
            Width = 2048,
            Height = 2048,
            Mipmaps = 1,
            Format = PixelFormat.UncompressedR32
        };
        ShadowFboId = Rlgl.LoadFramebuffer();
        Rlgl.FramebufferAttach(
            ShadowFboId,
            ShadowMapTexture.Id,
            FramebufferAttachType.Depth,
            FramebufferAttachTextureType.Texture2D,
            0);

        LoadLocations();
    }

    public bool Reload()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string vsPath = Path.Combine(dir, "lighting.vs");
        string fsPath = Path.Combine(dir, "lighting.fs");

        if (!File.Exists(vsPath) || !File.Exists(fsPath))
            return false;

        Shader newShader = Raylib.LoadShader(vsPath, fsPath);
        if (newShader.Id == 0 || newShader.Id == Rlgl.GetShaderIdDefault())
        {
            return false;
        }

        Raylib.UnloadShader(Shader);
        Shader = newShader;
        LoadLocations();
        return true;
    }

    private void LoadLocations()
    {
        _locViewPos = Raylib.GetShaderLocation(Shader, "viewPos");
        _locSunDir = Raylib.GetShaderLocation(Shader, "sunDir");
        _locSunColor = Raylib.GetShaderLocation(Shader, "sunColor");
        _locAmbient = Raylib.GetShaderLocation(Shader, "ambientColor");
        _locSpec = Raylib.GetShaderLocation(Shader, "specStrength");

        _locMvpLight = Raylib.GetShaderLocation(Shader, "mvpLight");
        _locShadowMap = Raylib.GetShaderLocation(Shader, "shadowMap");
    }

    /// <summary>Jednou per frame pred kreslenim sceny.</summary>
    public void Apply(Camera3D camera)
    {
        Raylib.SetShaderValue(Shader, _locViewPos, camera.Position, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locSunDir, SunDirection, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locSunColor, SunColor * SunIntensity, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locAmbient, Ambient, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locSpec, SpecStrength, ShaderUniformDataType.Float);

        // Stinove uniformy
        Raylib.SetShaderValueMatrix(Shader, _locMvpLight, LightMvp);
        Raylib.SetShaderValueTexture(Shader, _locShadowMap, ShadowMapTexture);
    }

    public void Dispose()
    {
        Raylib.UnloadShader(Shader);
        Rlgl.UnloadFramebuffer(ShadowFboId);
        Rlgl.UnloadTexture(ShadowMapTexture.Id);
    }
}
