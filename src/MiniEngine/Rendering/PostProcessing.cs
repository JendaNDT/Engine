using System.IO;
using Raylib_cs;

namespace MiniEngine.Rendering;

public sealed class PostProcessing : System.IDisposable
{
    public Shader Shader;
    public bool Enabled = true;
    public float BloomIntensity = 0.6f;
    public float BloomThreshold = 0.7f;
    public float VignettePower = 0.3f;

    private readonly int _locBloomIntensity;
    private readonly int _locBloomThreshold;
    private readonly int _locVignettePower;

    public PostProcessing()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        
        // Raylib LoadShader se zadanou prázdnou cestou pro VS načte výchozí raylib VS
        Shader = Raylib.LoadShader(null, Path.Combine(dir, "postprocess.fs"));

        _locBloomIntensity = Raylib.GetShaderLocation(Shader, "bloomIntensity");
        _locBloomThreshold = Raylib.GetShaderLocation(Shader, "bloomThreshold");
        _locVignettePower = Raylib.GetShaderLocation(Shader, "vignettePower");
    }

    public void Apply()
    {
        Raylib.SetShaderValue(Shader, _locBloomIntensity, BloomIntensity, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Shader, _locBloomThreshold, BloomThreshold, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Shader, _locVignettePower, VignettePower, ShaderUniformDataType.Float);
    }

    public void Dispose()
    {
        Raylib.UnloadShader(Shader);
    }
}
