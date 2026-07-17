using System.Numerics;
using MiniEngine.Assets;
using MiniEngine.Core;
using MiniEngine.Rendering;
using Raylib_cs;
using Transform = MiniEngine.Core.Transform;

namespace MiniEngine.Engine;

// ============================================================
// SceneRenderer – vykreslovací část vytažená z Game.cs
// (Fáze 40, krok 2). Sdílí ji editor i MiniPlayer.
//
// Obsahuje: CSM shadow pass (3 kaskády v atlasu 4096²),
// kreslení geometrií s PBR materiály a základní průchod scény
// (skybox + grid + geometrie + částice).
// V Game.cs zůstává: editorové 3D helpery (ikony světel/zvuků,
// trigger boxy, obrysy výběru, gizmo) – kreslí se PO DrawBase.
// ============================================================

public enum RenderMode
{
    Lit,
    Unlit,
    Wireframe
}

public sealed class SceneRenderer : IDisposable
{
    private readonly AssetManager _assets;
    private readonly LightingShader _lighting;
    private readonly SkyboxRenderer _skybox;
    private readonly ParticleSystem _particles;
    private readonly Store<Transform> _transforms;
    private readonly Store<MeshRenderer> _renderers;
    private readonly Store<ParticleEmitter> _emitters;

    private Mesh _cubeMesh;
    private Material _cubeMaterial;

    public RenderMode Mode = RenderMode.Lit;
    /// <summary>Aspect pro výpočet kaskád (editor 1600/900, přehrávač podle okna).</summary>
    public float Aspect = 1600f / 900f;

    public SceneRenderer(World world, AssetManager assets, LightingShader lighting,
        SkyboxRenderer skybox, ParticleSystem particles)
    {
        _assets = assets;
        _lighting = lighting;
        _skybox = skybox;
        _particles = particles;
        _transforms = world.Store<Transform>();
        _renderers = world.Store<MeshRenderer>();
        _emitters = world.Store<ParticleEmitter>();

        // Sdílený mesh/materiál pro entity bez modelu (dřív v Game.Run)
        _cubeMesh = Raylib.GenMeshCube(1f, 1f, 1f);
        _cubeMaterial = Raylib.LoadMaterialDefault();
        _cubeMaterial.Shader = _lighting.Shader;
    }

    /// <summary>Po hot-reloadu shaderu (Game.cs: _assets.OnAssetChanged).</summary>
    public void RefreshShader() => _cubeMaterial.Shader = _lighting.Shader;

    // ---------- Shadow pass (dřív inline v Game.Run) ----------

    public void ShadowPass(Camera3D camera)
    {
        RenderTexture2D shadowMapRT = new()
        {
            Id = _lighting.ShadowFboId,
            Texture = new Texture2D { Id = 0, Width = 4096, Height = 4096 },
            Depth = new Texture2D { Id = _lighting.ShadowMapTexture.Id, Width = 4096, Height = 4096 }
        };

        Raylib.BeginTextureMode(shadowMapRT);
        Rlgl.ColorMask(false, false, false, false);
        Rlgl.ClearScreenBuffers();

        RenderCascade(camera, 0.1f, 8.0f, 0, 2048, m => _lighting.LightMvp0 = m);
        RenderCascade(camera, 8.0f, 25.0f, 2048, 2048, m => _lighting.LightMvp1 = m);
        RenderCascade(camera, 25.0f, 75.0f, 0, 0, m => _lighting.LightMvp2 = m);

        Rlgl.ColorMask(true, true, true, true);
        Rlgl.Viewport(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        Raylib.EndTextureMode();
    }

    private void RenderCascade(Camera3D camera, float near, float far, int vpX, int vpY, Action<Matrix4x4> setMvp)
    {
        ComputeCascadeBounds(camera, near, far, out Vector3 center, out float radius);

        Camera3D shadowCamera = new()
        {
            Position = center - Vector3.Normalize(_lighting.SunDirection) * (radius * 2.0f),
            Target = center,
            Up = MathF.Abs(_lighting.SunDirection.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitZ,
            FovY = radius * 2.0f,
            Projection = CameraProjection.Orthographic
        };

        Rlgl.Viewport(vpX, vpY, 2048, 2048);
        Raylib.BeginMode3D(shadowCamera);
        setMvp(Rlgl.GetMatrixModelview() * Rlgl.GetMatrixProjection());
        DrawGeometries();
        Raylib.EndMode3D();
    }

    // ---------- Základní průchod scény (dřív začátek Game.DrawScene) ----------

    /// <summary>
    /// Skybox + osvětlení + grid + geometrie + částice. Editor po něm
    /// dokreslí helpery, obrysy a gizmo; přehrávač nic.
    /// </summary>
    public void DrawBase(Camera3D camera, bool showGrid = false, float gridSpacing = 1f)
    {
        _skybox.Draw(camera);
        _lighting.Apply(camera);   // viewPos + parametry slunce do shaderu

        if (showGrid && gridSpacing > 0f)
            Raylib.DrawGrid((int)(40f / gridSpacing), gridSpacing);

        DrawGeometries();
        _particles.Draw(camera, _assets);
    }

    // ---------- Geometrie (dřív Game.DrawSceneGeometries, beze změn) ----------

    public void DrawGeometries()
    {
        var renderers = _renderers.Span;
        var entities = _renderers.Entities;

        Shader currentShader = (Mode == RenderMode.Lit) ? _lighting.Shader : default;
        if (Mode == RenderMode.Wireframe) Rlgl.EnableWireMode();

        for (int i = 0; i < renderers.Length; i++)
        {
            ref var r = ref renderers[i];
            if (!r.Visible) continue;

            ref var t = ref _transforms.Get(entities[i]);

            if (r.ModelHandle >= 0)
            {
                var world = Matrix4x4.Transpose(t.World);
                ref Model m = ref _assets.Get(r.ModelHandle);

                Texture2D texOverride = default;
                if (!string.IsNullOrEmpty(r.AlbedoTexturePath))
                    texOverride = _assets.GetTexture(r.AlbedoTexturePath);

                unsafe
                {
                    for (int mi = 0; mi < m.MeshCount; mi++)
                    {
                        ref var mat = ref m.Materials[m.MeshMaterial[mi]];
                        Shader origShader = mat.Shader;
                        mat.Shader = currentShader;

                        Color origColor = mat.Maps[(int)MaterialMapIndex.Albedo].Color;
                        Texture2D origTex = mat.Maps[(int)MaterialMapIndex.Albedo].Texture;
                        Texture2D origNormal = mat.Maps[(int)MaterialMapIndex.Normal].Texture;
                        Texture2D origRoughness = mat.Maps[(int)MaterialMapIndex.Roughness].Texture;

                        byte rVal = (byte)Math.Clamp(origColor.R * r.Tint.X, 0f, 255f);
                        byte gVal = (byte)Math.Clamp(origColor.G * r.Tint.Y, 0f, 255f);
                        byte bVal = (byte)Math.Clamp(origColor.B * r.Tint.Z, 0f, 255f);
                        mat.Maps[(int)MaterialMapIndex.Albedo].Color = new Color(rVal, gVal, bVal, origColor.A);

                        if (texOverride.Id > 0)
                            mat.Maps[(int)MaterialMapIndex.Albedo].Texture = texOverride;

                        bool hasNormal = !string.IsNullOrEmpty(r.NormalMapPath);
                        if (hasNormal)
                        {
                            var normTex = _assets.GetTexture(r.NormalMapPath);
                            if (normTex.Id > 0) mat.Maps[(int)MaterialMapIndex.Normal].Texture = normTex;
                            else hasNormal = false;
                        }
                        Raylib.SetShaderValue(_lighting.Shader, _lighting.LocHasNormalMap, hasNormal ? 1 : 0, ShaderUniformDataType.Int);

                        bool hasMR = !string.IsNullOrEmpty(r.MetallicRoughnessMapPath);
                        if (hasMR)
                        {
                            var mrTex = _assets.GetTexture(r.MetallicRoughnessMapPath);
                            if (mrTex.Id > 0) mat.Maps[(int)MaterialMapIndex.Roughness].Texture = mrTex;
                            else hasMR = false;
                        }
                        Raylib.SetShaderValue(_lighting.Shader, _lighting.LocHasMetallicRoughnessMap, hasMR ? 1 : 0, ShaderUniformDataType.Int);

                        Raylib.SetShaderValue(_lighting.Shader, _lighting.LocMetallicFactor, r.MetallicFactor, ShaderUniformDataType.Float);
                        Raylib.SetShaderValue(_lighting.Shader, _lighting.LocRoughnessFactor, r.RoughnessFactor, ShaderUniformDataType.Float);

                        Raylib.DrawMesh(m.Meshes[mi], mat, world);

                        mat.Shader = origShader;
                        mat.Maps[(int)MaterialMapIndex.Albedo].Color = origColor;
                        mat.Maps[(int)MaterialMapIndex.Albedo].Texture = origTex;
                        mat.Maps[(int)MaterialMapIndex.Normal].Texture = origNormal;
                        mat.Maps[(int)MaterialMapIndex.Roughness].Texture = origRoughness;
                    }
                }
            }
            else
            {
                var color = new Color((byte)(r.Tint.X * 255), (byte)(r.Tint.Y * 255), (byte)(r.Tint.Z * 255), (byte)255);

                Texture2D texOverride = default;
                if (!string.IsNullOrEmpty(r.AlbedoTexturePath))
                    texOverride = _assets.GetTexture(r.AlbedoTexturePath);

                unsafe
                {
                    _cubeMaterial.Maps[(int)MaterialMapIndex.Albedo].Color = color;
                    Texture2D origTex = _cubeMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture;
                    Texture2D origNormal = _cubeMaterial.Maps[(int)MaterialMapIndex.Normal].Texture;
                    Texture2D origRoughness = _cubeMaterial.Maps[(int)MaterialMapIndex.Roughness].Texture;

                    if (texOverride.Id > 0)
                        _cubeMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = texOverride;

                    bool hasNormal = !string.IsNullOrEmpty(r.NormalMapPath);
                    if (hasNormal)
                    {
                        var normTex = _assets.GetTexture(r.NormalMapPath);
                        if (normTex.Id > 0) _cubeMaterial.Maps[(int)MaterialMapIndex.Normal].Texture = normTex;
                        else hasNormal = false;
                    }
                    Raylib.SetShaderValue(_lighting.Shader, _lighting.LocHasNormalMap, hasNormal ? 1 : 0, ShaderUniformDataType.Int);

                    bool hasMR = !string.IsNullOrEmpty(r.MetallicRoughnessMapPath);
                    if (hasMR)
                    {
                        var mrTex = _assets.GetTexture(r.MetallicRoughnessMapPath);
                        if (mrTex.Id > 0) _cubeMaterial.Maps[(int)MaterialMapIndex.Roughness].Texture = mrTex;
                        else hasMR = false;
                    }
                    Raylib.SetShaderValue(_lighting.Shader, _lighting.LocHasMetallicRoughnessMap, hasMR ? 1 : 0, ShaderUniformDataType.Int);

                    Raylib.SetShaderValue(_lighting.Shader, _lighting.LocMetallicFactor, r.MetallicFactor, ShaderUniformDataType.Float);
                    Raylib.SetShaderValue(_lighting.Shader, _lighting.LocRoughnessFactor, r.RoughnessFactor, ShaderUniformDataType.Float);

                    _cubeMaterial.Shader = currentShader;
                    Raylib.DrawMesh(_cubeMesh, _cubeMaterial, Matrix4x4.Transpose(t.World));

                    _cubeMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = origTex;
                    _cubeMaterial.Maps[(int)MaterialMapIndex.Normal].Texture = origNormal;
                    _cubeMaterial.Maps[(int)MaterialMapIndex.Roughness].Texture = origRoughness;
                }
            }
        }

        if (Mode == RenderMode.Wireframe) Rlgl.DisableWireMode();
    }

    // ---------- Kaskády (dřív Game.ComputeCascadeBounds, jen Aspect je pole) ----------

    private void ComputeCascadeBounds(Camera3D camera, float nearSplit, float farSplit, out Vector3 center, out float radius)
    {
        Vector3 camPos = camera.Position;
        Vector3 camForward = Vector3.Normalize(camera.Target - camPos);
        Vector3 camRight = Vector3.Normalize(Vector3.Cross(camForward, camera.Up));
        Vector3 camUp = Vector3.Cross(camRight, camForward);

        float fovRad = camera.FovY * MathF.PI / 180f;
        float nearHeight = 2f * MathF.Tan(fovRad / 2f) * nearSplit;
        float nearWidth = nearHeight * Aspect;
        float farHeight = 2f * MathF.Tan(fovRad / 2f) * farSplit;
        float farWidth = farHeight * Aspect;

        Vector3 nearCenter = camPos + camForward * nearSplit;
        Vector3 farCenter = camPos + camForward * farSplit;

        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = nearCenter + (camUp * nearHeight * 0.5f) - (camRight * nearWidth * 0.5f);
        corners[1] = nearCenter + (camUp * nearHeight * 0.5f) + (camRight * nearWidth * 0.5f);
        corners[2] = nearCenter - (camUp * nearHeight * 0.5f) - (camRight * nearWidth * 0.5f);
        corners[3] = nearCenter - (camUp * nearHeight * 0.5f) + (camRight * nearWidth * 0.5f);
        corners[4] = farCenter + (camUp * farHeight * 0.5f) - (camRight * farWidth * 0.5f);
        corners[5] = farCenter + (camUp * farHeight * 0.5f) + (camRight * farWidth * 0.5f);
        corners[6] = farCenter - (camUp * farHeight * 0.5f) - (camRight * farWidth * 0.5f);
        corners[7] = farCenter - (camUp * farHeight * 0.5f) + (camRight * farWidth * 0.5f);

        center = Vector3.Zero;
        foreach (var c in corners) center += c;
        center /= 8f;

        radius = 0f;
        foreach (var c in corners)
        {
            float dist = Vector3.Distance(c, center);
            if (dist > radius) radius = dist;
        }
        radius = MathF.Ceiling(radius * 8f) / 8f;
    }

    public void Dispose()
    {
        Raylib.UnloadMesh(_cubeMesh);
    }
}
