using System.IO;
using System.Numerics;
using Raylib_cs;

namespace MiniEngine.Rendering;

public sealed class SkyboxRenderer : System.IDisposable
{
    public Shader Shader;
    public Texture2D PanoramaTexture;
    private readonly Mesh _cubeMesh;
    private readonly Material _material;
    
    public string SkyboxTexturePath = "";

    public SkyboxRenderer()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        Shader = Raylib.LoadShader(
            Path.Combine(dir, "skybox.vs"),
            Path.Combine(dir, "skybox.fs"));

        _cubeMesh = Raylib.GenMeshCube(1f, 1f, 1f);
        _material = Raylib.LoadMaterialDefault();
        _material.Shader = Shader;
    }

    public void LoadSkybox(string relativePath)
    {
        string fullPath = Path.Combine(AppContext.BaseDirectory, "assets", relativePath);
        if (!File.Exists(fullPath)) return;

        if (PanoramaTexture.Id > 0)
        {
            Raylib.UnloadTexture(PanoramaTexture);
        }

        PanoramaTexture = Raylib.LoadTexture(fullPath);
        SkyboxTexturePath = relativePath;

        unsafe
        {
            _material.Maps[(int)MaterialMapIndex.Albedo].Texture = PanoramaTexture;
        }
    }

    public void Draw(Camera3D camera)
    {
        if (PanoramaTexture.Id == 0) return;

        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();

        Matrix4x4 modelMatrix = Matrix4x4.CreateScale(10f) * Matrix4x4.CreateTranslation(camera.Position);
        Raylib.DrawMesh(_cubeMesh, _material, Matrix4x4.Transpose(modelMatrix));

        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    public void Dispose()
    {
        Raylib.UnloadShader(Shader);
        Raylib.UnloadMesh(_cubeMesh);
        if (PanoramaTexture.Id > 0)
        {
            Raylib.UnloadTexture(PanoramaTexture);
        }
    }
}
