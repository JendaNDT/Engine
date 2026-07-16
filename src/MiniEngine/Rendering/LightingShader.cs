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

    // Shadow Mapping (CSM)
    public uint ShadowFboId;
    public Texture2D ShadowMapTexture;
    public Matrix4x4 LightMvp0;
    public Matrix4x4 LightMvp1;
    public Matrix4x4 LightMvp2;
    private int _locMvpLight0;
    private int _locMvpLight1;
    private int _locMvpLight2;
    private int _locShadowMap;

    public int LocHasNormalMap;
    public int LocHasMetallicRoughnessMap;
    public int LocMetallicFactor;
    public int LocRoughnessFactor;

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

        // Inicializace stinové mapy (CSM - Atlas 4096x4096)
        ShadowMapTexture = new Texture2D
        {
            Id = Rlgl.LoadTextureDepth(4096, 4096, false),
            Width = 4096,
            Height = 4096,
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

        _locMvpLight0 = Raylib.GetShaderLocation(Shader, "mvpLight0");
        _locMvpLight1 = Raylib.GetShaderLocation(Shader, "mvpLight1");
        _locMvpLight2 = Raylib.GetShaderLocation(Shader, "mvpLight2");
        _locShadowMap = Raylib.GetShaderLocation(Shader, "shadowMap");

        LocHasNormalMap = Raylib.GetShaderLocation(Shader, "hasNormalMap");
        LocHasMetallicRoughnessMap = Raylib.GetShaderLocation(Shader, "hasMetallicRoughnessMap");
        LocMetallicFactor = Raylib.GetShaderLocation(Shader, "metallicFactor");
        LocRoughnessFactor = Raylib.GetShaderLocation(Shader, "roughnessFactor");
    }

    /// <summary>Jednou per frame pred kreslenim sceny.</summary>
    public void Apply(Camera3D camera)
    {
        Raylib.SetShaderValue(Shader, _locViewPos, camera.Position, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locSunDir, SunDirection, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locSunColor, SunColor * SunIntensity, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locAmbient, Ambient, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _locSpec, SpecStrength, ShaderUniformDataType.Float);

        // Stinove uniformy (CSM)
        Raylib.SetShaderValueMatrix(Shader, _locMvpLight0, LightMvp0);
        Raylib.SetShaderValueMatrix(Shader, _locMvpLight1, LightMvp1);
        Raylib.SetShaderValueMatrix(Shader, _locMvpLight2, LightMvp2);
        Raylib.SetShaderValueTexture(Shader, _locShadowMap, ShadowMapTexture);
    }

    public void Dispose()
    {
        Raylib.UnloadShader(Shader);
        Rlgl.UnloadFramebuffer(ShadowFboId);
        Rlgl.UnloadTexture(ShadowMapTexture.Id);
    }
}
