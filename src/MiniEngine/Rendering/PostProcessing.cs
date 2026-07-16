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
    public float Saturation = 1.0f;
    public float Contrast = 1.0f;
    public float ChromaticAberration = 0.0f;

    private readonly int _locBloomIntensity;
    private readonly int _locBloomThreshold;
    private readonly int _locVignettePower;
    private readonly int _locSaturation;
    private readonly int _locContrast;
    private readonly int _locChromaticAberration;

    public PostProcessing()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        
        // Raylib LoadShader se zadanou prázdnou cestou pro VS načte výchozí raylib VS
        Shader = Raylib.LoadShader(null, Path.Combine(dir, "postprocess.fs"));

        _locBloomIntensity = Raylib.GetShaderLocation(Shader, "bloomIntensity");
        _locBloomThreshold = Raylib.GetShaderLocation(Shader, "bloomThreshold");
        _locVignettePower = Raylib.GetShaderLocation(Shader, "vignettePower");
        _locSaturation = Raylib.GetShaderLocation(Shader, "saturation");
        _locContrast = Raylib.GetShaderLocation(Shader, "contrast");
        _locChromaticAberration = Raylib.GetShaderLocation(Shader, "chromaticAberration");
    }

    public void Apply()
    {
        Raylib.SetShaderValue(Shader, _locBloomIntensity, BloomIntensity, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Shader, _locBloomThreshold, BloomThreshold, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Shader, _locVignettePower, VignettePower, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Shader, _locSaturation, Saturation, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Shader, _locContrast, Contrast, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Shader, _locChromaticAberration, ChromaticAberration, ShaderUniformDataType.Float);
    }

    public void Dispose()
    {
        Raylib.UnloadShader(Shader);
    }
}
