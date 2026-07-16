using System.Numerics;
using ImGuiNET;
using MiniEngine.Core;
using Transform = MiniEngine.Core.Transform;

namespace MiniEngine.Editor;

/// <summary>
/// Editace vlastnosti vybrane entity.
/// Rotace: KVATERNION je zdroj pravdy. Eulery jsou jen docasna cache pro UI -
/// bez ni by hodnoty v polich "preskakovaly" pri rotaci pres 90 stupnu (gimbal).
/// </summary>
public sealed class InspectorPanel
{
    private readonly Store<Transform> _transforms;
    private readonly Store<MeshRenderer> _renderers;
    private readonly Store<Name> _names;
    private readonly Store<ParticleEmitter> _emitters;
    private readonly Store<AudioSourceComponent> _audioSources;
    private readonly Store<BehaviorComponent> _behaviors;
    private readonly Store<TriggerComponent> _triggers;
    private readonly Store<ActionComponent> _actions;
    private readonly Store<LightComponent> _lights;
    private readonly Store<PrefabLink> _prefabLinks;

    public Action? OnFocus;
    public Action? OnChanged;
    public Action<int>? OnRevertPrefab;
    public Action<int>? OnApplyPrefab;

    private string[] _availableTextures = [];
    private string[] _availableAudioClips = [];

    // Kešované seznamy pro comba (zamezení GC alokacím)
    private string[] _comboTexItems = [];
    private string[] _comboNormItems = [];
    private string[] _comboMRItems = [];
    private string[] _comboParticleTexItems = [];
    private string[] _comboAudioItems = [];

    // Euler cache plati jen pro entitu, pro kterou byla spocitana.
    private int _eulerEntity = -1;
    private Vector3 _eulerDeg;

    /// <summary>
    /// Zahodi euler cache. Vola Game, kdyz rotaci zmeni neco jineho nez Inspector
    /// (rotate gizmo) - jinak by pole rotace ukazovala stare hodnoty az do zmeny vyberu.
    /// </summary>
    public void InvalidateRotationCache() => _eulerEntity = -1;

    public InspectorPanel(World world)
    {
        _transforms = world.Store<Transform>();
        _renderers = world.Store<MeshRenderer>();
        _names = world.Store<Name>();
        _emitters = world.Store<ParticleEmitter>();
        _audioSources = world.Store<AudioSourceComponent>();
        _behaviors = world.Store<BehaviorComponent>();
        _triggers = world.Store<TriggerComponent>();
        _actions = world.Store<ActionComponent>();
        _lights = world.Store<LightComponent>();
        _prefabLinks = world.Store<PrefabLink>();

        ScanAssets();
    }

    public void ScanAssets()
    {
        string texturesDir = Path.Combine(AppContext.BaseDirectory, "assets", "textures");
        if (Directory.Exists(texturesDir))
        {
            var files = Directory.GetFiles(texturesDir, "*.*", SearchOption.AllDirectories);
            var list = new System.Collections.Generic.List<string>();
            foreach (var f in files)
            {
                string ext = Path.GetExtension(f).ToLower();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tga")
                {
                    string rel = Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "assets"), f);
                    list.Add(rel);
                }
            }
            _availableTextures = list.ToArray();
        }
        else
        {
            _availableTextures = [];
        }

        string audioDir = Path.Combine(AppContext.BaseDirectory, "assets", "audio");
        if (Directory.Exists(audioDir))
        {
            var files = Directory.GetFiles(audioDir, "*.*", SearchOption.AllDirectories);
            var list = new System.Collections.Generic.List<string>();
            foreach (var f in files)
            {
                string ext = Path.GetExtension(f).ToLower();
                if (ext == ".wav" || ext == ".ogg" || ext == ".mp3")
                {
                    string rel = Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "assets"), f);
                    list.Add(rel);
                }
            }
            _availableAudioClips = list.ToArray();
        }
        else
        {
            _availableAudioClips = [];
        }

        // Naplnění kešovaných combo seznamů
        _comboTexItems = new string[_availableTextures.Length + 1];
        _comboTexItems[0] = "(Žádná textura)";
        Array.Copy(_availableTextures, 0, _comboTexItems, 1, _availableTextures.Length);

        _comboNormItems = new string[_availableTextures.Length + 1];
        _comboNormItems[0] = "(Bez normálové mapy)";
        Array.Copy(_availableTextures, 0, _comboNormItems, 1, _availableTextures.Length);

        _comboMRItems = new string[_availableTextures.Length + 1];
        _comboMRItems[0] = "(Bez metal/roughness mapy)";
        Array.Copy(_availableTextures, 0, _comboMRItems, 1, _availableTextures.Length);

        _comboParticleTexItems = new string[_availableTextures.Length + 1];
        _comboParticleTexItems[0] = "(Bez textury - kostky)";
        Array.Copy(_availableTextures, 0, _comboParticleTexItems, 1, _availableTextures.Length);

        _comboAudioItems = new string[_availableAudioClips.Length + 1];
        _comboAudioItems[0] = "(Žádný zvuk)";
        Array.Copy(_availableAudioClips, 0, _comboAudioItems, 1, _availableAudioClips.Length);
    }

    public void Draw(EditorSelection selection)
    {
        ImGui.Begin("Inspector");
        DrawInner(selection);
        ImGui.End();
    }

    public void DrawInner(EditorSelection selection)
    {
        int e = selection.EntityIndex;
        if (e < 0 || !_transforms.Has(e))
        {
            ImGui.TextDisabled("Nic nevybrano");
            return;
        }

        // Sirka poli: nech 110 px vpravo na popisky, jinak se oriznou mimo okno.
        ImGui.PushItemWidth(-110f);

        if (ImGui.Button("Obnovit assety (Znovu načíst)"))
        {
            ScanAssets();
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"[Tex: {_availableTextures.Length}, Aud: {_availableAudioClips.Length}]");

        // --- Nazev ---
        if (_names.Has(e))
        {
            ref var n = ref _names.Get(e);
            string buf = n.Value ?? "";
            if (ImGui.InputText("Nazev", ref buf, 64))
                n.Value = buf;
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
        }

        // --- Prefab Link ---
        if (MiniEngine.Engine.SceneSerializer.IsPartOfPrefab(_transforms, _prefabLinks, e, out int prefabRoot))
        {
            var pl = _prefabLinks.Get(prefabRoot);
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.85f, 0.5f, 0.2f, 0.2f));
            if (ImGui.CollapsingHeader($"🧩 Prefab Instance ({System.IO.Path.GetFileName(pl.PrefabPath)})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                ImGui.TextDisabled($"Cesta: assets/{pl.PrefabPath}");
                if (e != prefabRoot)
                {
                    ImGui.TextDisabled("Vybraný objekt je součástí prefabu.");
                }

                // Počet overrides
                var overrides = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
                int overrideCount = 0;
                if (!string.IsNullOrEmpty(pl.Overrides))
                {
                    try
                    {
                        var deserialized = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>>(pl.Overrides);
                        if (deserialized != null)
                        {
                            overrides = deserialized;
                            foreach (var kvp in overrides)
                            {
                                if (kvp.Value != null) overrideCount += kvp.Value.Count;
                            }
                        }
                    }
                    catch {}
                }
                ImGui.Text($"Lokální změny (overrides): {overrideCount}");

                if (overrideCount > 0)
                {
                    if (ImGui.TreeNode("Detail změn..."))
                    {
                        foreach (var kvp in overrides)
                        {
                            int relIdx = int.Parse(kvp.Key);
                            string entName = $"Entity ({relIdx})";
                            var sceneSubtree = new System.Collections.Generic.List<int>();
                            MiniEngine.Engine.SceneSerializer.GetDfsSubtree(_transforms, prefabRoot, sceneSubtree);
                            if (relIdx >= 0 && relIdx < sceneSubtree.Count)
                            {
                                int sEnt = sceneSubtree[relIdx];
                                if (_names.Has(sEnt)) entName = _names.Get(sEnt).Value;
                            }

                            foreach (var prop in kvp.Value)
                            {
                                ImGui.TextDisabled($"- {entName}: {prop}");
                            }
                        }
                        ImGui.TreePop();
                    }
                }

                ImGui.Spacing();
                if (ImGui.Button("Revert to Prefab"))
                {
                    OnRevertPrefab?.Invoke(prefabRoot);
                }
                ImGui.SameLine();
                if (ImGui.Button("Apply to Prefab"))
                {
                    OnApplyPrefab?.Invoke(prefabRoot);
                }
                ImGui.Unindent();
                ImGui.Separator();
            }
            ImGui.PopStyleColor();
        }

        // --- Transform ---
        ImGui.Separator();
        if (ImGui.CollapsingHeader("📐 Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
        if (OnFocus != null)
        {
            ImGui.SameLine(ImGui.GetWindowWidth() - 110f);
            if (ImGui.Button("Focus"))
            {
                OnFocus.Invoke();
            }
        }

        ref var t = ref _transforms.Get(e);

        ImGui.DragFloat3("Pozice", ref t.Position, 0.05f);
        if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

        if (e != _eulerEntity)
        {
            _eulerDeg = ToEulerDegrees(t.Rotation);
            _eulerEntity = e;
        }
        if (ImGui.DragFloat3("Rotace", ref _eulerDeg, 0.5f))
        {
            t.Rotation = Quaternion.CreateFromYawPitchRoll(
                Deg2Rad(_eulerDeg.Y),   // yaw   = okolo Y
                Deg2Rad(_eulerDeg.X),   // pitch = okolo X
                Deg2Rad(_eulerDeg.Z));  // roll  = okolo Z
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

        if (ImGui.DragFloat3("Meritko", ref t.Scale, 0.05f))
        {
            t.Scale.X = MathF.Max(0.001f, t.Scale.X);
            t.Scale.Y = MathF.Max(0.001f, t.Scale.Y);
            t.Scale.Z = MathF.Max(0.001f, t.Scale.Z);
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
        }

        // --- MeshRenderer ---
        if (_renderers.Has(e))
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader("📦 Mesh Renderer", ImGuiTreeNodeFlags.DefaultOpen))
            {

            ref var r = ref _renderers.Get(e);
            ImGui.Checkbox("Viditelny", ref r.Visible);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
            ImGui.ColorEdit3("Barva", ref r.Tint);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

            string currentTex = r.AlbedoTexturePath ?? "";
            int selectedIdx = Array.IndexOf(_availableTextures, currentTex);

            int comboSelect = selectedIdx + 1; // 0 = Zadne
            if (ImGui.Combo("Textura", ref comboSelect, _comboTexItems, _comboTexItems.Length))
            {
                r.AlbedoTexturePath = comboSelect == 0 ? "" : _comboTexItems[comboSelect];
                OnChanged?.Invoke();
            }
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_TEXTURE_PATH");
                unsafe
                {
                    if (payload.NativePtr != null)
                    {
                        int length = payload.DataSize;
                        byte* dataPtr = (byte*)payload.Data;
                        byte[] bytes = new byte[length];
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)dataPtr, bytes, 0, length);
                        r.AlbedoTexturePath = System.Text.Encoding.UTF8.GetString(bytes);
                        OnChanged?.Invoke();
                    }
                }
                ImGui.EndDragDropTarget();
            }

            string currentNorm = r.NormalMapPath ?? "";
            int selectedNormIdx = Array.IndexOf(_availableTextures, currentNorm);
            int comboNormSelect = selectedNormIdx + 1;
            if (ImGui.Combo("Normálová mapa", ref comboNormSelect, _comboNormItems, _comboNormItems.Length))
            {
                r.NormalMapPath = comboNormSelect == 0 ? "" : _comboNormItems[comboNormSelect];
                OnChanged?.Invoke();
            }
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_TEXTURE_PATH");
                unsafe
                {
                    if (payload.NativePtr != null)
                    {
                        int length = payload.DataSize;
                        byte* dataPtr = (byte*)payload.Data;
                        byte[] bytes = new byte[length];
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)dataPtr, bytes, 0, length);
                        r.NormalMapPath = System.Text.Encoding.UTF8.GetString(bytes);
                        OnChanged?.Invoke();
                    }
                }
                ImGui.EndDragDropTarget();
            }

            string currentMR = r.MetallicRoughnessMapPath ?? "";
            int selectedMRIdx = Array.IndexOf(_availableTextures, currentMR);
            int comboMRSelect = selectedMRIdx + 1;
            if (ImGui.Combo("Metal/Roughness mapa", ref comboMRSelect, _comboMRItems, _comboMRItems.Length))
            {
                r.MetallicRoughnessMapPath = comboMRSelect == 0 ? "" : _comboMRItems[comboMRSelect];
                OnChanged?.Invoke();
            }
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_TEXTURE_PATH");
                unsafe
                {
                    if (payload.NativePtr != null)
                    {
                        int length = payload.DataSize;
                        byte* dataPtr = (byte*)payload.Data;
                        byte[] bytes = new byte[length];
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)dataPtr, bytes, 0, length);
                        r.MetallicRoughnessMapPath = System.Text.Encoding.UTF8.GetString(bytes);
                        OnChanged?.Invoke();
                    }
                }
                ImGui.EndDragDropTarget();
            }

            // --- Factors ---
            ImGui.SliderFloat("Kovovost (Metallic)", ref r.MetallicFactor, 0f, 1f);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

            ImGui.SliderFloat("Drsnost (Roughness)", ref r.RoughnessFactor, 0f, 1f);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
            }
        }

        // --- ParticleEmitter ---
        if (_emitters.Has(e))
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader("✨ Particle Emitter", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ref var emitter = ref _emitters.Get(e);
                ImGui.Checkbox("Aktivni", ref emitter.Active);
                if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

                string[] types = ["Oheň", "Kouř", "Jiskry"];
                int currentType = emitter.Type;
                if (ImGui.Combo("Preset typu", ref currentType, types, types.Length))
                {
                    emitter.Type = currentType;
                    if (currentType == 0) // Ohen
                    {
                        emitter.ColorStart = new Vector3(1.0f, 0.5f, 0.1f);
                        emitter.ColorEnd = new Vector3(0.2f, 0.2f, 0.2f);
                        emitter.SizeStart = 0.2f;
                        emitter.SizeMiddle = 0.15f;
                        emitter.SizeEnd = 0.05f;
                        emitter.Gravity = new Vector3(0f, 1f, 0f);
                        emitter.VelocityMin = new Vector3(-0.5f, 1f, -0.5f);
                        emitter.VelocityMax = new Vector3(0.5f, 2f, 0.5f);
                    }
                    else if (currentType == 1) // Kour
                    {
                        emitter.ColorStart = new Vector3(0.4f, 0.4f, 0.4f);
                        emitter.ColorEnd = new Vector3(0.8f, 0.8f, 0.8f);
                        emitter.SizeStart = 0.1f;
                        emitter.SizeMiddle = 0.3f;
                        emitter.SizeEnd = 0.5f;
                        emitter.Gravity = new Vector3(0.2f, 0.5f, 0f);
                        emitter.VelocityMin = new Vector3(-0.2f, 0.5f, -0.2f);
                        emitter.VelocityMax = new Vector3(0.2f, 1.2f, 0.2f);
                    }
                    else if (currentType == 2) // Jiskry
                    {
                        emitter.ColorStart = new Vector3(1.0f, 0.9f, 0.3f);
                        emitter.ColorEnd = new Vector3(1.0f, 0.1f, 0f);
                        emitter.SizeStart = 0.08f;
                        emitter.SizeMiddle = 0.04f;
                        emitter.SizeEnd = 0.01f;
                        emitter.Gravity = new Vector3(0f, -4f, 0f);
                        emitter.VelocityMin = new Vector3(-1.5f, 2f, -1.5f);
                        emitter.VelocityMax = new Vector3(1.5f, 5f, 1.5f);
                    }
                    OnChanged?.Invoke();
                }

                // Sekce: Emise
                if (ImGui.TreeNode("Emise"))
                {
                    ImGui.DragFloat("Spawn Rate", ref emitter.SpawnRate, 0.5f, 1f, 200f);
                    if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
                    ImGui.DragFloat("Životnost částic", ref emitter.Lifetime, 0.05f, 0.1f, 10f);
                    if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
                    ImGui.TreePop();
                }

                // Sekce: Vzhled
                if (ImGui.TreeNode("Vzhled"))
                {
                    ImGui.ColorEdit3("Barva Start", ref emitter.ColorStart);
                    if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
                    ImGui.ColorEdit3("Barva End", ref emitter.ColorEnd);
                    if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
                    ImGui.DragFloat("Velikost Start", ref emitter.SizeStart, 0.01f, 0.01f, 2f);
                    if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
                    ImGui.DragFloat("Velikost Střed", ref emitter.SizeMiddle, 0.01f, 0.01f, 2f);
                    if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
                    ImGui.DragFloat("Velikost End", ref emitter.SizeEnd, 0.01f, 0.01f, 2f);
                    if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

                    string currentTex = emitter.TexturePath ?? "";
                    int selectedIdx = Array.IndexOf(_availableTextures, currentTex);

                    int comboSelect = selectedIdx + 1;
                    if (ImGui.Combo("Textura částic", ref comboSelect, _comboParticleTexItems, _comboParticleTexItems.Length))
                    {
                        emitter.TexturePath = comboSelect == 0 ? "" : _comboParticleTexItems[comboSelect];
                        OnChanged?.Invoke();
                    }
                    if (ImGui.BeginDragDropTarget())
                    {
                        var payload = ImGui.AcceptDragDropPayload("ASSET_TEXTURE_PATH");
                        unsafe
                        {
                            if (payload.NativePtr != null)
                            {
                                int length = payload.DataSize;
                                byte* dataPtr = (byte*)payload.Data;
                                byte[] bytes = new byte[length];
                                System.Runtime.InteropServices.Marshal.Copy((IntPtr)dataPtr, bytes, 0, length);
                                emitter.TexturePath = System.Text.Encoding.UTF8.GetString(bytes);
                                OnChanged?.Invoke();
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                    ImGui.TreePop();
                }

                // Sekce: Fyzika a pohyb
                if (ImGui.TreeNode("Fyzika a pohyb"))
                {
                    ImGui.DragFloat3("Gravitace", ref emitter.Gravity, 0.05f);
                    if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
                    ImGui.DragFloat3("Rychlost Min", ref emitter.VelocityMin, 0.05f);
                    if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
                    ImGui.DragFloat3("Rychlost Max", ref emitter.VelocityMax, 0.05f);
                    if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
                    ImGui.TreePop();
                }

                ImGui.Spacing();
                if (ImGui.Button("Odebrat ParticleEmitter"))
                {
                    _emitters.RemoveAt(e);
                    OnChanged?.Invoke();
                }
            }
        }
        else
        {
            ImGui.Separator();
            if (ImGui.Button("Přidat ParticleEmitter"))
            {
                _emitters.Add(e, ParticleEmitter.Default);
                OnChanged?.Invoke();
            }
        }

        // --- AudioSourceComponent ---
        if (_audioSources.Has(e))
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader("🔊 Audio Source", ImGuiTreeNodeFlags.DefaultOpen))
            {

            ref var source = ref _audioSources.Get(e);
            ImGui.Checkbox("Aktivni (Prehravat)", ref source.Active);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

            string currentClip = source.ClipPath ?? "";
            int selectedIdx = Array.IndexOf(_availableAudioClips, currentClip);

            int comboSelect = selectedIdx + 1; // 0 = Zadne
            if (ImGui.Combo("Zvukový soubor", ref comboSelect, _comboAudioItems, _comboAudioItems.Length))
            {
                source.ClipPath = comboSelect == 0 ? "" : _comboAudioItems[comboSelect];
                OnChanged?.Invoke();
            }
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_AUDIO_PATH");
                unsafe
                {
                    if (payload.NativePtr != null)
                    {
                        int length = payload.DataSize;
                        byte* dataPtr = (byte*)payload.Data;
                        byte[] bytes = new byte[length];
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)dataPtr, bytes, 0, length);
                        source.ClipPath = System.Text.Encoding.UTF8.GetString(bytes);
                        OnChanged?.Invoke();
                    }
                }
                ImGui.EndDragDropTarget();
            }

            ImGui.SliderFloat("Hlasitost", ref source.Volume, 0f, 1f);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
            ImGui.SliderFloat("Pitch", ref source.Pitch, 0.5f, 2f);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
            ImGui.Checkbox("Smyckovat", ref source.Loop);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
            ImGui.Checkbox("Prostorovy 3D", ref source.Spatial3D);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

            if (ImGui.Button("Odebrat AudioSource"))
            {
                _audioSources.RemoveAt(e);
                OnChanged?.Invoke();
            }
            }
        }
        else
        {
            ImGui.Separator();
            if (ImGui.Button("Přidat AudioSource"))
            {
                _audioSources.Add(e, AudioSourceComponent.Default);
                OnChanged?.Invoke();
            }
        }

        // --- BehaviorComponent ---
        if (_behaviors.Has(e))
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader("⚙️ Behavior (Skript pohybu)", ImGuiTreeNodeFlags.DefaultOpen))
            {

            ref var b = ref _behaviors.Get(e);
            ImGui.Checkbox("Aktivni (Spustit se simulaci)", ref b.Active);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

            string[] types = ["Rotátor", "Ping-Pong pohyb", "Obíhač", "Sinusové houpání"];
            int currentType = b.Type;
            if (ImGui.Combo("Typ pohybu", ref currentType, types, types.Length))
            {
                b.Type = currentType;
                if (currentType == 0) // Rotator
                {
                    b.Axis = Vector3.UnitY;
                    b.Speed = 1f;
                }
                else if (currentType == 1) // Ping-Pong
                {
                    b.Axis = Vector3.UnitX;
                    b.Speed = 2f;
                    b.Range = 3f;
                }
                else if (currentType == 2) // Orbiter
                {
                    b.Speed = 1f;
                    b.Range = 4f;
                }
                else if (currentType == 3) // Bobber
                {
                    b.Frequency = 0.5f;
                    b.Range = 1f;
                }
                OnChanged?.Invoke();
            }

            if (b.Type == 0 || b.Type == 1)
            {
                ImGui.DragFloat3("Osa pohybu/rotace", ref b.Axis, 0.05f);
                if (b.Axis != Vector3.Zero) b.Axis = Vector3.Normalize(b.Axis);
                if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
            }

            ImGui.DragFloat("Rychlost", ref b.Speed, 0.05f);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
            
            if (b.Type == 1 || b.Type == 2 || b.Type == 3)
            {
                ImGui.DragFloat("Rozsah (Range)", ref b.Range, 0.05f, 0f, 100f);
                if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
            }

            if (b.Type == 3)
            {
                ImGui.DragFloat("Frekvence", ref b.Frequency, 0.01f, 0.01f, 10f);
                if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();
            }

            if (ImGui.Button("Odebrat Behavior"))
            {
                _behaviors.RemoveAt(e);
                OnChanged?.Invoke();
            }
            }
        }
        else
        {
            ImGui.Separator();
            if (ImGui.Button("Přidat Behavior"))
            {
                _behaviors.Add(e, BehaviorComponent.Default);
                OnChanged?.Invoke();
            }
        }

        // --- TriggerComponent ---
        if (_triggers.Has(e))
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader("⚡ Trigger (Spouštěč kolizí)", ImGuiTreeNodeFlags.DefaultOpen))
            {

            ref var trigger = ref _triggers.Get(e);
            ImGui.Checkbox("Aktivní Trigger", ref trigger.Active);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

            ImGui.DragFloat3("Velikost zóny", ref trigger.Size, 0.05f, 0.1f, 50f);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

            // Vyhledání všech entit ve scéně (přes Transform) pro spárování cílů
            var trEntities = _transforms.Entities;
            var entityNames = new System.Collections.Generic.List<string> { "(Žádný cíl)" };
            var entityIndices = new System.Collections.Generic.List<int> { -1 };
            for (int idx = 0; idx < trEntities.Length; idx++)
            {
                int entIndex = trEntities[idx];
                if (entIndex == e) continue; // ignorovat sebe sama

                string nameStr = _names.Has(entIndex) ? _names.Get(entIndex).Value : $"Entita #{entIndex}";
                entityNames.Add($"[{entIndex}] {nameStr}");
                entityIndices.Add(entIndex);
            }

            int currentTargetIndex = entityIndices.IndexOf(trigger.TargetEntity);
            if (currentTargetIndex < 0)
            {
                currentTargetIndex = 0;
                if (trigger.TargetEntity != -1)
                {
                    // Cíl byl smazán z hierarchie -> vyčistíme ID cílů na -1
                    trigger.TargetEntity = -1;
                    OnChanged?.Invoke();
                }
            }
            string[] entityComboItems = entityNames.ToArray();
            if (ImGui.Combo("Cílový objekt akce", ref currentTargetIndex, entityComboItems, entityComboItems.Length))
            {
                trigger.TargetEntity = entityIndices[currentTargetIndex];
                OnChanged?.Invoke();
            }

            if (ImGui.Button("Odebrat Trigger"))
            {
                _triggers.RemoveAt(e);
                OnChanged?.Invoke();
            }
            }
        }
        else
        {
            ImGui.Separator();
            if (ImGui.Button("Přidat Trigger"))
            {
                _triggers.Add(e, TriggerComponent.Default);
                OnChanged?.Invoke();
            }
        }

        // --- ActionComponent ---
        if (_actions.Has(e))
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader("⚡ Action (Akce)", ImGuiTreeNodeFlags.DefaultOpen))
            {

            ref var action = ref _actions.Get(e);
            ImGui.Checkbox("Aktivní Akce", ref action.Active);
            if (ImGui.IsItemDeactivatedAfterEdit()) OnChanged?.Invoke();

            string[] actionTypes = ["Spustit zvuk", "Přepnout částice (Emit)", "Přepnout viditelnost"];
            int actType = action.ActionType;
            if (ImGui.Combo("Typ akce", ref actType, actionTypes, actionTypes.Length))
            {
                action.ActionType = actType;
                OnChanged?.Invoke();
            }

            if (action.ActionType == 0)
            {
                string currentClip = action.ActionParam ?? "";
                int selectedAudioIdx = Array.IndexOf(_availableAudioClips, currentClip);

                int comboSelect = selectedAudioIdx + 1;
                if (ImGui.Combo("Zvukový soubor", ref comboSelect, _comboAudioItems, _comboAudioItems.Length))
                {
                    action.ActionParam = comboSelect == 0 ? "" : _comboAudioItems[comboSelect];
                    OnChanged?.Invoke();
                }
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_AUDIO_PATH");
                    unsafe
                    {
                        if (payload.NativePtr != null)
                        {
                            int length = payload.DataSize;
                            byte* dataPtr = (byte*)payload.Data;
                            byte[] bytes = new byte[length];
                            System.Runtime.InteropServices.Marshal.Copy((IntPtr)dataPtr, bytes, 0, length);
                            action.ActionParam = System.Text.Encoding.UTF8.GetString(bytes);
                            OnChanged?.Invoke();
                        }
                    }
                    ImGui.EndDragDropTarget();
                }
            }
            else if (action.ActionType == 1)
            {
                ImGui.TextDisabled("Akce spustí/zastaví ParticleEmitter na této entitě.");
            }
            else if (action.ActionType == 2)
            {
                ImGui.TextDisabled("Akce přepne viditelnost MeshRendereru na této entitě.");
            }

            if (ImGui.Button("Odebrat Akci"))
            {
                _actions.RemoveAt(e);
                OnChanged?.Invoke();
            }
            }
        }
        else
        {
            ImGui.Separator();
            if (ImGui.Button("Přidat Akci"))
            {
                _actions.Add(e, ActionComponent.Default);
                OnChanged?.Invoke();
            }
        }

        // --- SVĚTLO ---
        if (_lights.Has(e))
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader("💡 Světlo", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ref var l = ref _lights.Get(e);
                bool changed = false;

                if (ImGui.Checkbox("Aktivní světlo", ref l.Active)) changed = true;
                
                ImGui.ColorEdit3("Barva světla", ref l.Color);
                if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;
                
                ImGui.SliderFloat("Poloměr (Radius)", ref l.Radius, 0.1f, 50f, "%.1f m");
                if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;
                
                ImGui.SliderFloat("Intenzita světla", ref l.Intensity, 0f, 10f, "%.1f");
                if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

                if (changed)
                {
                    OnChanged?.Invoke();
                }

                if (ImGui.Button("Odebrat Světlo"))
                {
                    _lights.RemoveAt(e);
                    OnChanged?.Invoke();
                }
            }
        }
        else
        {
            ImGui.Separator();
            if (ImGui.Button("Přidat Světlo"))
            {
                _lights.Add(e, LightComponent.Default);
                OnChanged?.Invoke();
            }
        }

        ImGui.PopItemWidth();
    }

    private static float Deg2Rad(float deg) => deg * MathF.PI / 180f;

    /// <summary>
    /// Kvaternion -> Eulery ve stupnich (X=pitch, Y=yaw, Z=roll).
    /// Inverze k Quaternion.CreateFromYawPitchRoll.
    /// </summary>
    private static Vector3 ToEulerDegrees(Quaternion q)
    {
        float sinPitch = Math.Clamp(2f * (q.W * q.X - q.Y * q.Z), -1f, 1f);
        float pitch = MathF.Asin(sinPitch);

        float yaw = MathF.Atan2(
            2f * (q.W * q.Y + q.X * q.Z),
            1f - 2f * (q.X * q.X + q.Y * q.Y));

        float roll = MathF.Atan2(
            2f * (q.W * q.Z + q.X * q.Y),
            1f - 2f * (q.X * q.X + q.Z * q.Z));

        const float toDeg = 180f / MathF.PI;
        return new Vector3(pitch * toDeg, yaw * toDeg, roll * toDeg);
    }
}
