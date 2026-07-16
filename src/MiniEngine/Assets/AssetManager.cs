using Raylib_cs;

namespace MiniEngine.Assets;

/// <summary>
/// Handle-based, refcounted cache modelu.
///
/// PROC handle a ne primo Model:
/// Raylib Model/Mesh/Material jsou struktury s NATIVNIMI POINTERY. Kdyz je zkopirujes
/// hodnotou a pak na kopii zavolas UnloadModel, uvolnis pamet sdilenou s originalem
/// -> use-after-free, pad bez stack trace. Proto Model zije jen tady a ven jde int.
/// </summary>
public sealed class AssetManager : IDisposable
{
    /// <summary>Koren assetu vedle exe - funguje pro 'dotnet run' i dvojklik na publish.</summary>
    public static string AssetsRoot => Path.Combine(AppContext.BaseDirectory, "assets");

    private readonly Dictionary<string, int> _byPath = [];
    private Model[] _models = new Model[32];
    private int[] _refCounts = new int[32];
    private string[] _paths = new string[32];
    private int _count;

    private readonly System.IO.FileSystemWatcher? _watcher;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _changedPaths = new();

    public Action<string>? OnAssetChanged;

    public int Count => _count;

    public AssetManager()
    {
        if (System.IO.Directory.Exists(AssetsRoot))
        {
            _watcher = new System.IO.FileSystemWatcher(AssetsRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _watcher.Changed += (s, e) => { if (!string.IsNullOrEmpty(e.FullPath)) _changedPaths.Enqueue(e.FullPath); };
            _watcher.Renamed += (s, e) => { if (!string.IsNullOrEmpty(e.FullPath)) _changedPaths.Enqueue(e.FullPath); };
        }
    }

    /// <summary>Shader prirazeny kazdemu nove nactenemu modelu (osvetleni). Nastavuje Game.</summary>
    public Shader? DefaultShader;

    public int LoadModel(string path)
    {
        string key = Path.GetFullPath(path);

        if (_byPath.TryGetValue(key, out int id))
        {
            _refCounts[id]++;
            return id;
        }

        if (_count == _models.Length)
        {
            Array.Resize(ref _models, _count * 2);
            Array.Resize(ref _refCounts, _count * 2);
            Array.Resize(ref _paths, _count * 2);
        }

        // Raylib zvladne .glb / .gltf / .obj / .iqm / .m3d
        _models[_count] = Raylib.LoadModel(key);
        _refCounts[_count] = 1;
        _paths[_count] = key;
        _byPath[key] = _count;

        if (DefaultShader is { } shader)
            SetShader(_count, shader);

        return _count++;
    }

    /// <summary>VZDY 'ref' - navrat hodnotou by znamenal kopii struktury s pointery.</summary>
    public ref Model Get(int handle) => ref _models[handle];

    /// <summary>
    /// Prihlaseni dalsiho drzitele existujiciho handlu (duplikace entity).
    /// Bez toho by refcount nesedel a dvoji Release (napr. pri nacteni sceny)
    /// by zavolal UnloadModel dvakrat -> double free v nativnim kodu.
    /// </summary>
    public void AddRef(int handle)
    {
        if (handle >= 0 && handle < _count) _refCounts[handle]++;
    }

    /// <summary>Cesta modelu relativni k assets/ s '/' oddelovaci - do ulozene sceny (prenositelne mezi OS).</summary>
    public string GetRelativePath(int handle) =>
        Path.GetRelativePath(AssetsRoot, _paths[handle]).Replace('\\', '/');

    public void Release(int handle)
    {
        // Pojistka '<= 0': Release na uz uvolneny handle nesmi znovu odpalit UnloadModel.
        if (handle < 0 || handle >= _count || _refCounts[handle] <= 0) return;
        if (--_refCounts[handle] > 0) return;

        ResetToDefaultShader(handle);   // sdileny shader NESMI zemrit s modelem
        Raylib.UnloadModel(_models[handle]);
        _byPath.Remove(_paths[handle]);
        _refCounts[handle] = 0;
    }

    /// <summary>
    /// UnloadModel vola UnloadMaterial a ten by SMAZAL i prirazeny sdileny shader
    /// (presne past z DEEP_RESEARCH 4.1). Pred unloadem se materialum vrati
    /// raylib default shader, ktery unload prezije.
    /// </summary>
    private unsafe void ResetToDefaultShader(int handle)
    {
        var defaultShader = new Shader
        {
            Id = Rlgl.GetShaderIdDefault(),
            Locs = Rlgl.GetShaderLocsDefault()
        };

        ref Model model = ref _models[handle];
        for (int i = 0; i < model.MaterialCount; i++)
            model.Materials[i].Shader = defaultShader;
    }

    /// <summary>
    /// Prirazeni vlastniho shaderu vsem materialum modelu.
    /// Model.Materials je Material* -> potrebujes unsafe (AllowUnsafeBlocks v csproj).
    ///
    /// POZOR: UnloadModel uvolni i materialy a jejich textury. Pokud jsi materialu
    /// priradil SDILENOU texturu z jineho zdroje, raylib ti ji pri unloadu odstreli.
    /// Nejjednodussi obrana: sdilene shadery a textury neuvolnuj vubec (do konce behu).
    /// </summary>
    public unsafe void SetShader(int handle, Shader shader)
    {
        ref Model model = ref _models[handle];
        for (int i = 0; i < model.MaterialCount; i++)
            model.Materials[i].Shader = shader;
    }

    /// <summary>Kosterni animace z GLTF. Raylib umi jednu skin, zadne morph targets.</summary>
    public unsafe ModelAnimation* LoadAnimations(string path, out int count)
    {
        int c = 0;
        var anims = Raylib.LoadModelAnimations(Path.GetFullPath(path), ref c);
        count = c;
        return anims;
    }

    public void UpdateHotReload()
    {
        while (_changedPaths.TryDequeue(out string? fullPath))
        {
            if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath)) continue;

            string key = System.IO.Path.GetFullPath(fullPath);

            if (_byPath.TryGetValue(key, out int id))
            {
                unsafe
                {
                    Shader? activeShader = null;
                    if (_models[id].MaterialCount > 0)
                    {
                        activeShader = _models[id].Materials[0].Shader;
                    }

                    ResetToDefaultShader(id);
                    Raylib.UnloadModel(_models[id]);

                    _models[id] = Raylib.LoadModel(key);

                    if (activeShader.HasValue)
                    {
                        for (int i = 0; i < _models[id].MaterialCount; i++)
                            _models[id].Materials[i].Shader = activeShader.Value;
                    }
                    else if (DefaultShader is { } defShader)
                    {
                        SetShader(id, defShader);
                    }
                }

                OnAssetChanged?.Invoke(key);
            }
            else
            {
                OnAssetChanged?.Invoke(key);
            }
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();

        for (int i = 0; i < _count; i++)
        {
            if (_refCounts[i] > 0)
            {
                ResetToDefaultShader(i);
                Raylib.UnloadModel(_models[i]);
            }
        }

        _count = 0;
        _byPath.Clear();
    }
}
