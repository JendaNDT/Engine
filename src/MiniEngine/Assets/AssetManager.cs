using System;
using System.IO;
using System.Collections.Generic;
using Raylib_cs;
using MiniEngine.Core;

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
    private readonly Dictionary<string, Texture2D> _textures = [];
    private readonly Dictionary<string, Sound> _sounds = [];
    private readonly List<int> _freeSlots = [];
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

        int targetIndex;
        if (_freeSlots.Count > 0)
        {
            int lastIdx = _freeSlots.Count - 1;
            targetIndex = _freeSlots[lastIdx];
            _freeSlots.RemoveAt(lastIdx);
        }
        else
        {
            targetIndex = _count;
            if (_count == _models.Length)
            {
                Array.Resize(ref _models, _count * 2);
                Array.Resize(ref _refCounts, _count * 2);
                Array.Resize(ref _paths, _count * 2);
            }
            _count++;
        }

        // Raylib zvladne .glb / .gltf / .obj / .iqm / .m3d
        _models[targetIndex] = Raylib.LoadModel(key);
        _refCounts[targetIndex] = 1;
        _paths[targetIndex] = key;
        _byPath[key] = targetIndex;

        if (DefaultShader is { } shader)
            SetShader(targetIndex, shader);

        return targetIndex;
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
        _paths[handle] = null!;
        _models[handle] = default;
        _freeSlots.Add(handle);
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

    private readonly Dictionary<string, List<Sound>> _aliasesByPath = [];

    public void UpdateHotReload()
    {
        while (_changedPaths.TryDequeue(out string? fullPath))
        {
            if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath)) continue;

            string key = System.IO.Path.GetFullPath(fullPath);

            if (_byPath.TryGetValue(key, out int id))
            {
                // Krátká prodleva k uvolnění zámku souboru externím programem
                System.Threading.Thread.Sleep(150);

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

    public void ClearCache()
    {
        foreach (var tex in _textures.Values)
        {
            Raylib.UnloadTexture(tex);
        }
        _textures.Clear();

        foreach (var list in _aliasesByPath.Values)
        {
            foreach (var alias in list)
            {
                Raylib.UnloadSound(alias);
            }
        }
        _aliasesByPath.Clear();

        foreach (var sound in _sounds.Values)
        {
            Raylib.UnloadSound(sound);
        }
        _sounds.Clear();
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

        ClearCache();
        _count = 0;
        _byPath.Clear();
    }

    public Texture2D GetTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) return default;

        string fullPath = Path.Combine(AssetsRoot, path);
        if (!File.Exists(fullPath))
        {
            fullPath = path;
            if (!File.Exists(fullPath)) return default;
        }

        string key = Path.GetFullPath(fullPath);

        if (_textures.TryGetValue(key, out var tex))
        {
            return tex;
        }

        var newTex = Raylib.LoadTexture(key);
        _textures[key] = newTex;
        return newTex;
    }

    public Sound GetSound(string path)
    {
        if (string.IsNullOrEmpty(path)) return default;

        string fullPath = Path.Combine(AssetsRoot, path);
        if (!File.Exists(fullPath))
        {
            fullPath = path;
            if (!File.Exists(fullPath)) return default;
        }

        string key = Path.GetFullPath(fullPath);

        if (!_sounds.TryGetValue(key, out var originalSound))
        {
            originalSound = Raylib.LoadSound(key);
            _sounds[key] = originalSound;
        }

        // Vytvořit samostatný alias zvuku pro nezávislé ovládání
        var alias = Raylib.LoadSoundAlias(originalSound);
        if (!_aliasesByPath.TryGetValue(key, out var list))
        {
            list = [];
            _aliasesByPath[key] = list;
        }
        list.Add(alias);
        return alias;
    }

    public void CollectUnusedAssets(
        ReadOnlySpan<MeshRenderer> renderers,
        ReadOnlySpan<ParticleEmitter> emitters,
        ReadOnlySpan<AudioSourceComponent> audioSources)
    {
        var referencedTex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referencedSounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Získat všechny odkazované textury
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (!string.IsNullOrEmpty(r.AlbedoTexturePath))
                referencedTex.Add(Path.GetFullPath(Path.Combine(AssetsRoot, r.AlbedoTexturePath)));
            if (!string.IsNullOrEmpty(r.NormalMapPath))
                referencedTex.Add(Path.GetFullPath(Path.Combine(AssetsRoot, r.NormalMapPath)));
            if (!string.IsNullOrEmpty(r.MetallicRoughnessMapPath))
                referencedTex.Add(Path.GetFullPath(Path.Combine(AssetsRoot, r.MetallicRoughnessMapPath)));
        }

        for (int i = 0; i < emitters.Length; i++)
        {
            var e = emitters[i];
            if (!string.IsNullOrEmpty(e.TexturePath))
                referencedTex.Add(Path.GetFullPath(Path.Combine(AssetsRoot, e.TexturePath)));
        }

        // 2. Získat všechny odkazované zvuky
        for (int i = 0; i < audioSources.Length; i++)
        {
            var s = audioSources[i];
            if (!string.IsNullOrEmpty(s.ClipPath))
                referencedSounds.Add(Path.GetFullPath(Path.Combine(AssetsRoot, s.ClipPath)));
        }

        // 3. Uvolnit nepoužívané textury
        var texKeys = new List<string>(_textures.Keys);
        foreach (var key in texKeys)
        {
            if (!referencedTex.Contains(key))
            {
                Raylib.UnloadTexture(_textures[key]);
                _textures.Remove(key);
            }
        }

        // 4. Uvolnit nepoužívané zvuky a jejich aliasy
        var soundKeys = new List<string>(_sounds.Keys);
        foreach (var key in soundKeys)
        {
            if (!referencedSounds.Contains(key))
            {
                if (_aliasesByPath.TryGetValue(key, out var aliases))
                {
                    foreach (var alias in aliases)
                    {
                        Raylib.UnloadSound(alias);
                    }
                    _aliasesByPath.Remove(key);
                }

                Raylib.UnloadSound(_sounds[key]);
                _sounds.Remove(key);
            }
        }
    }
}
