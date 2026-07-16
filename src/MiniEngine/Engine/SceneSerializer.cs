using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniEngine.Assets;
using MiniEngine.Core;
using Transform = MiniEngine.Core.Transform;
using MiniEngine.Rendering;

namespace MiniEngine.Engine;

// ---------------------------------------------------------------------------
// DTO tridy pro JSON. Zamerne oddelene od runtime komponent:
// - ModelHandle (int) je runtime vec -> uklada se RELATIVNI CESTA k assets/
// - Parent (index entity) je runtime vec -> uklada se POZICE v seznamu Entities
// ---------------------------------------------------------------------------

public sealed class SceneData
{
    public List<EntityData> Entities { get; set; } = [];
    public LightingSettingsData? Lighting { get; set; }
    public PostProcessingSettingsData? PostProcessing { get; set; }
}

public sealed class PostProcessingSettingsData
{
    public bool Enabled { get; set; } = true;
    public float BloomIntensity { get; set; } = 0.6f;
    public float BloomThreshold { get; set; } = 0.7f;
    public float VignettePower { get; set; } = 0.3f;
    public float Saturation { get; set; } = 1.0f;
    public float Contrast { get; set; } = 1.0f;
    public float ChromaticAberration { get; set; } = 0.0f;
}

public sealed class LightingSettingsData
{
    public float[] SunDirection { get; set; } = [0, 0, 0];
    public float[] SunColor { get; set; } = [1, 1, 1];
    public float SunIntensity { get; set; } = 1.0f;
    public float[] Ambient { get; set; } = [0.2f, 0.2f, 0.2f];
    public float SpecStrength { get; set; } = 0.25f;
}

public sealed class EntityData
{
    public string Name { get; set; } = "";
    public float[] Position { get; set; } = [0, 0, 0];
    public float[] Rotation { get; set; } = [0, 0, 0, 1];   // kvaternion XYZW
    public float[] Scale { get; set; } = [1, 1, 1];
    public int Parent { get; set; } = -1;                   // pozice rodice v Entities, -1 = root
    public string? PrefabPath { get; set; }                  // null = neni prefab
    public string? PrefabOverrides { get; set; }             // JSON string s prepsanymi vlastnostmi
    public MeshRendererData? MeshRenderer { get; set; }
    public ParticleEmitterData? ParticleEmitter { get; set; }
    public AudioSourceData? AudioSource { get; set; }
    public BehaviorData? Behavior { get; set; }
    public TriggerData? Trigger { get; set; }
    public ActionData? Action { get; set; }
    public LightData? Light { get; set; }
}

public sealed class MeshRendererData
{
    public string? ModelPath { get; set; }                  // null = placeholder krychle
    public float[] Tint { get; set; } = [1, 1, 1];
    public bool Visible { get; set; } = true;
    public string? AlbedoTexturePath { get; set; }
    public string? NormalMapPath { get; set; }
    public string? MetallicRoughnessMapPath { get; set; }
    public float MetallicFactor { get; set; }
    public float RoughnessFactor { get; set; }
}

public sealed class ParticleEmitterData
{
    public bool Active { get; set; }
    public int Type { get; set; }
    public float SpawnRate { get; set; }
    public float[] ColorStart { get; set; } = [1, 0.5f, 0.1f];
    public float[] ColorEnd { get; set; } = [0.2f, 0.2f, 0.2f];
    public float SizeStart { get; set; } = 0.2f;
    public float SizeMiddle { get; set; } = 0.1f;
    public float SizeEnd { get; set; } = 0.05f;
    public float Lifetime { get; set; } = 1.5f;
    public float[] Gravity { get; set; } = [0, 1, 0];
    public float[] VelocityMin { get; set; } = [-0.5f, 1f, -0.5f];
    public float[] VelocityMax { get; set; } = [0.5f, 2f, 0.5f];
    public string? TexturePath { get; set; }
}

public sealed class AudioSourceData
{
    public bool Active { get; set; }
    public string? ClipPath { get; set; }
    public float Volume { get; set; } = 1f;
    public float Pitch { get; set; } = 1f;
    public bool Loop { get; set; }
    public bool Spatial3D { get; set; } = true;
}

public sealed class BehaviorData
{
    public bool Active { get; set; }
    public int Type { get; set; }
    public float[] Axis { get; set; } = [0, 1, 0];
    public float Speed { get; set; } = 1f;
    public float Range { get; set; } = 2f;
    public float Frequency { get; set; } = 1f;
}

public sealed class TriggerData
{
    public bool Active { get; set; }
    public float[] Size { get; set; } = [1f, 1f, 1f];
    public int TargetEntity { get; set; } = -1;
}

public sealed class ActionData
{
    public bool Active { get; set; }
    public int ActionType { get; set; }
    public string? ActionParam { get; set; }
}

public sealed class LightData
{
    public bool Active { get; set; }
    public float[] Color { get; set; } = [1f, 0.9f, 0.7f];
    public float Radius { get; set; } = 5f;
    public float Intensity { get; set; } = 1f;
}

/// <summary>
/// Source-generated JSON kontext - POVINNE kvuli NativeAOT.
/// Reflexni serializace by v AOT publish spadla (viz DEEP_RESEARCH 2.3).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SceneData))]
[JsonSerializable(typeof(ParticleEmitterData))]
[JsonSerializable(typeof(AudioSourceData))]
[JsonSerializable(typeof(BehaviorData))]
[JsonSerializable(typeof(TriggerData))]
[JsonSerializable(typeof(ActionData))]
[JsonSerializable(typeof(PostProcessingSettingsData))]
[JsonSerializable(typeof(LightData))]
internal partial class SceneJsonContext : JsonSerializerContext { }

public static class SceneSerializer
{
    public static SceneData CreateSceneData(Store<Transform> transforms, Store<MeshRenderer> renderers,
        Store<Name> names, Store<ParticleEmitter> emitters, Store<AudioSourceComponent> audioSources, Store<BehaviorComponent> behaviors,
        Store<TriggerComponent> triggers, Store<ActionComponent> actions, Store<LightComponent> lights, AssetManager assets, LightingShader lighting, PostProcessing postProcessor,
        Store<PrefabLink> prefabLinks)
    {
        var scene = new SceneData();
        var entityIndexes = transforms.Entities;
        var span = transforms.Span;

        // Mapa: index entity -> pozice v ukladanem seznamu (kvuli remapu Parent).
        var listPos = new Dictionary<int, int>();
        for (int i = 0; i < entityIndexes.Length; i++)
            listPos[entityIndexes[i]] = i;

        for (int i = 0; i < span.Length; i++)
        {
            ref var t = ref span[i];
            int idx = entityIndexes[i];

            var e = new EntityData
            {
                Name = names.Has(idx) ? names.Get(idx).Value : $"Entity {idx}",
                Position = [t.Position.X, t.Position.Y, t.Position.Z],
                Rotation = [t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W],
                Scale = [t.Scale.X, t.Scale.Y, t.Scale.Z],
                Parent = t.Parent >= 0 && listPos.TryGetValue(t.Parent, out int p) ? p : -1
            };

            if (prefabLinks != null && prefabLinks.Has(idx))
            {
                var pl = prefabLinks.Get(idx);
                e.PrefabPath = pl.PrefabPath;
                e.PrefabOverrides = pl.Overrides;
            }

            if (renderers.Has(idx))
            {
                ref var r = ref renderers.Get(idx);
                e.MeshRenderer = new MeshRendererData
                {
                    ModelPath = r.ModelHandle >= 0 ? assets.GetRelativePath(r.ModelHandle) : null,
                    Tint = [r.Tint.X, r.Tint.Y, r.Tint.Z],
                    Visible = r.Visible,
                    AlbedoTexturePath = r.AlbedoTexturePath,
                    NormalMapPath = r.NormalMapPath,
                    MetallicRoughnessMapPath = r.MetallicRoughnessMapPath,
                    MetallicFactor = r.MetallicFactor,
                    RoughnessFactor = r.RoughnessFactor
                };
            }

            if (emitters.Has(idx))
            {
                ref var pe = ref emitters.Get(idx);
                e.ParticleEmitter = new ParticleEmitterData
                {
                    Active = pe.Active,
                    Type = pe.Type,
                    SpawnRate = pe.SpawnRate,
                    ColorStart = [pe.ColorStart.X, pe.ColorStart.Y, pe.ColorStart.Z],
                    ColorEnd = [pe.ColorEnd.X, pe.ColorEnd.Y, pe.ColorEnd.Z],
                    SizeStart = pe.SizeStart,
                    SizeMiddle = pe.SizeMiddle,
                    SizeEnd = pe.SizeEnd,
                    Lifetime = pe.Lifetime,
                    Gravity = [pe.Gravity.X, pe.Gravity.Y, pe.Gravity.Z],
                    VelocityMin = [pe.VelocityMin.X, pe.VelocityMin.Y, pe.VelocityMin.Z],
                    VelocityMax = [pe.VelocityMax.X, pe.VelocityMax.Y, pe.VelocityMax.Z],
                    TexturePath = pe.TexturePath
                };
            }

            if (audioSources.Has(idx))
            {
                ref var src = ref audioSources.Get(idx);
                e.AudioSource = new AudioSourceData
                {
                    Active = src.Active,
                    ClipPath = src.ClipPath,
                    Volume = src.Volume,
                    Pitch = src.Pitch,
                    Loop = src.Loop,
                    Spatial3D = src.Spatial3D
                };
            }

            if (behaviors.Has(idx))
            {
                ref var b = ref behaviors.Get(idx);
                e.Behavior = new BehaviorData
                {
                    Active = b.Active,
                    Type = b.Type,
                    Axis = [b.Axis.X, b.Axis.Y, b.Axis.Z],
                    Speed = b.Speed,
                    Range = b.Range,
                    Frequency = b.Frequency
                };
            }

            if (triggers.Has(idx))
            {
                ref var tr = ref triggers.Get(idx);
                e.Trigger = new TriggerData
                {
                    Active = tr.Active,
                    Size = [tr.Size.X, tr.Size.Y, tr.Size.Z],
                    TargetEntity = tr.TargetEntity >= 0 && listPos.TryGetValue(tr.TargetEntity, out int targetP) ? targetP : -1
                };
            }

            if (actions.Has(idx))
            {
                ref var act = ref actions.Get(idx);
                e.Action = new ActionData
                {
                    Active = act.Active,
                    ActionType = act.ActionType,
                    ActionParam = act.ActionParam
                };
            }

            if (lights.Has(idx))
            {
                ref var l = ref lights.Get(idx);
                e.Light = new LightData
                {
                    Active = l.Active,
                    Color = [l.Color.X, l.Color.Y, l.Color.Z],
                    Radius = l.Radius,
                    Intensity = l.Intensity
                };
            }

            scene.Entities.Add(e);
        }

        scene.Lighting = new LightingSettingsData
        {
            SunDirection = [lighting.SunDirection.X, lighting.SunDirection.Y, lighting.SunDirection.Z],
            SunColor = [lighting.SunColor.X, lighting.SunColor.Y, lighting.SunColor.Z],
            SunIntensity = lighting.SunIntensity,
            Ambient = [lighting.Ambient.X, lighting.Ambient.Y, lighting.Ambient.Z],
            SpecStrength = lighting.SpecStrength
        };

        scene.PostProcessing = new PostProcessingSettingsData
        {
            Enabled = postProcessor.Enabled,
            BloomIntensity = postProcessor.BloomIntensity,
            BloomThreshold = postProcessor.BloomThreshold,
            VignettePower = postProcessor.VignettePower,
            Saturation = postProcessor.Saturation,
            Contrast = postProcessor.Contrast,
            ChromaticAberration = postProcessor.ChromaticAberration
        };

        return scene;
    }

    public static void ApplySceneData(SceneData scene, World world, Store<Transform> transforms, Store<MeshRenderer> renderers,
        Store<Name> names, Store<ParticleEmitter> emitters, Store<AudioSourceComponent> audioSources, Store<BehaviorComponent> behaviors,
        Store<TriggerComponent> triggers, Store<ActionComponent> actions, Store<LightComponent> lights, AssetManager assets, LightingShader lighting, PostProcessing postProcessor,
        Store<PrefabLink> prefabLinks)
    {
        if (scene.Lighting is { } l)
        {
            if (l.SunDirection is { Length: >= 3 })
                lighting.SunDirection = new Vector3(l.SunDirection[0], l.SunDirection[1], l.SunDirection[2]);
            if (l.SunColor is { Length: >= 3 })
                lighting.SunColor = new Vector3(l.SunColor[0], l.SunColor[1], l.SunColor[2]);
            lighting.SunIntensity = l.SunIntensity;
            if (l.Ambient is { Length: >= 3 })
                lighting.Ambient = new Vector3(l.Ambient[0], l.Ambient[1], l.Ambient[2]);
            lighting.SpecStrength = l.SpecStrength;
        }

        if (scene.PostProcessing is { } pp)
        {
            postProcessor.Enabled = pp.Enabled;
            postProcessor.BloomIntensity = pp.BloomIntensity;
            postProcessor.BloomThreshold = pp.BloomThreshold;
            postProcessor.VignettePower = pp.VignettePower;
            postProcessor.Saturation = pp.Saturation;
            postProcessor.Contrast = pp.Contrast;
            postProcessor.ChromaticAberration = pp.ChromaticAberration;
        }

        // 1) Smazat soucasnou scenu = vsechno, co ma Transform.
        var current = transforms.Entities;
        var toDestroy = new int[current.Length];
        current.CopyTo(toDestroy);

        foreach (int idx in toDestroy)
        {
            if (renderers.Has(idx))
            {
                ref var r = ref renderers.Get(idx);
                if (r.ModelHandle >= 0) assets.Release(r.ModelHandle);
            }
            if (prefabLinks != null && prefabLinks.Has(idx))
            {
                prefabLinks.RemoveAt(idx);
            }
            world.Destroy(world.EntityFromIndex(idx));
        }

        // Vyčistit keš unmanaged textur a zvuků po smazání scény
        assets.ClearCache();

        // 2) Nejdriv vytvorit vsechny entity (kvuli remapu Parent), pak plnit komponenty.
        var created = new int[scene.Entities.Count];
        for (int i = 0; i < scene.Entities.Count; i++)
            created[i] = world.Create().Index;

        for (int i = 0; i < scene.Entities.Count; i++)
        {
            var e = scene.Entities[i];
            int idx = created[i];

            var t = Transform.Identity;
            if (e.Position is { Length: >= 3 })
                t.Position = new Vector3(e.Position[0], e.Position[1], e.Position[2]);
            if (e.Rotation is { Length: >= 4 })
                t.Rotation = new Quaternion(e.Rotation[0], e.Rotation[1], e.Rotation[2], e.Rotation[3]);
            if (e.Scale is { Length: >= 3 })
                t.Scale = new Vector3(e.Scale[0], e.Scale[1], e.Scale[2]);
            t.Parent = (e.Parent >= 0 && e.Parent < created.Length) ? created[e.Parent] : -1;
            transforms.Add(idx, t);

            names.Add(idx, new Name { Value = e.Name });

            if (e.MeshRenderer is { } mr)
            {
                int handle = mr.ModelPath is null
                    ? -1
                    : assets.LoadModel(Path.Combine(AssetManager.AssetsRoot, mr.ModelPath));

                renderers.Add(idx, new MeshRenderer
                {
                    ModelHandle = handle,
                    Tint = mr.Tint is { Length: >= 3 } ? new Vector3(mr.Tint[0], mr.Tint[1], mr.Tint[2]) : Vector3.One,
                    Visible = mr.Visible,
                    AlbedoTexturePath = mr.AlbedoTexturePath ?? "",
                    NormalMapPath = mr.NormalMapPath ?? "",
                    MetallicRoughnessMapPath = mr.MetallicRoughnessMapPath ?? "",
                    MetallicFactor = mr.MetallicFactor,
                    RoughnessFactor = mr.RoughnessFactor
                });
            }

            if (e.ParticleEmitter is { } pe)
            {
                emitters.Add(idx, new ParticleEmitter
                {
                    Active = pe.Active,
                    Type = pe.Type,
                    SpawnRate = pe.SpawnRate,
                    ColorStart = pe.ColorStart is { Length: >= 3 } ? new Vector3(pe.ColorStart[0], pe.ColorStart[1], pe.ColorStart[2]) : Vector3.One,
                    ColorEnd = pe.ColorEnd is { Length: >= 3 } ? new Vector3(pe.ColorEnd[0], pe.ColorEnd[1], pe.ColorEnd[2]) : Vector3.One,
                    SizeStart = pe.SizeStart,
                    SizeMiddle = pe.SizeMiddle,
                    SizeEnd = pe.SizeEnd,
                    Lifetime = pe.Lifetime,
                    Gravity = pe.Gravity is { Length: >= 3 } ? new Vector3(pe.Gravity[0], pe.Gravity[1], pe.Gravity[2]) : Vector3.Zero,
                    VelocityMin = pe.VelocityMin is { Length: >= 3 } ? new Vector3(pe.VelocityMin[0], pe.VelocityMin[1], pe.VelocityMin[2]) : Vector3.Zero,
                    VelocityMax = pe.VelocityMax is { Length: >= 3 } ? new Vector3(pe.VelocityMax[0], pe.VelocityMax[1], pe.VelocityMax[2]) : Vector3.Zero,
                    TexturePath = pe.TexturePath ?? ""
                });
            }

            if (e.AudioSource is { } src)
            {
                audioSources.Add(idx, new AudioSourceComponent
                {
                    Active = src.Active,
                    ClipPath = src.ClipPath ?? "",
                    Volume = src.Volume,
                    Pitch = src.Pitch,
                    Loop = src.Loop,
                    Spatial3D = src.Spatial3D,
                    IsPlaying = false
                });
            }

            if (e.Behavior is { } b)
            {
                behaviors.Add(idx, new BehaviorComponent
                {
                    Active = b.Active,
                    Type = b.Type,
                    Axis = b.Axis is { Length: >= 3 } ? new Vector3(b.Axis[0], b.Axis[1], b.Axis[2]) : Vector3.UnitY,
                    Speed = b.Speed,
                    Range = b.Range,
                    Frequency = b.Frequency,
                    Initialized = false
                });
            }

            if (e.Trigger is { } tr)
            {
                triggers.Add(idx, new TriggerComponent
                {
                    Active = tr.Active,
                    Size = new Vector3(tr.Size[0], tr.Size[1], tr.Size[2]),
                    TargetEntity = tr.TargetEntity, // Bude renamovano nize
                    WasTriggered = false
                });
            }

            if (e.Action is { } act)
            {
                actions.Add(idx, new ActionComponent
                {
                    Active = act.Active,
                    ActionType = act.ActionType,
                    ActionParam = act.ActionParam ?? "",
                    TriggeredState = false
                });
            }

            if (e.Light is { } ld)
            {
                lights.Add(idx, new LightComponent
                {
                    Active = ld.Active,
                    Color = new Vector3(ld.Color[0], ld.Color[1], ld.Color[2]),
                    Radius = ld.Radius,
                    Intensity = ld.Intensity
                });
            }

            if (prefabLinks != null && !string.IsNullOrEmpty(e.PrefabPath))
            {
                prefabLinks.Add(idx, new PrefabLink
                {
                    PrefabPath = e.PrefabPath,
                    Overrides = e.PrefabOverrides ?? ""
                });
            }
        }

        // Remap TargetEntity pro spoustec / akce
        for (int i = 0; i < scene.Entities.Count; i++)
        {
            var e = scene.Entities[i];
            int idx = created[i];
            if (e.Trigger is { } tr && tr.TargetEntity >= 0 && tr.TargetEntity < created.Length)
            {
                ref var triggerComp = ref triggers.Get(idx);
                triggerComp.TargetEntity = created[tr.TargetEntity];
            }
        }

        if (prefabLinks != null)
        {
            PropagatePrefabs(world, transforms, renderers, names, emitters, audioSources, behaviors, triggers, actions, lights, assets, prefabLinks);
        }
    }

    public static SceneData CreatePrefabData(int rootIdx, Store<Transform> transforms, Store<MeshRenderer> renderers,
        Store<Name> names, Store<ParticleEmitter> emitters, Store<AudioSourceComponent> audioSources, Store<BehaviorComponent> behaviors,
        Store<TriggerComponent> triggers, Store<ActionComponent> actions, Store<LightComponent> lights, AssetManager assets,
        Store<PrefabLink> prefabLinks)
    {
        var scene = new SceneData();
        if (!transforms.Has(rootIdx)) return scene;

        var entityIndexes = transforms.Entities;
        var subtree = new List<int> { rootIdx };
        for (int i = 0; i < entityIndexes.Length; i++)
        {
            int ent = entityIndexes[i];
            if (ent != rootIdx && TransformHierarchy.IsDescendantOf(transforms, ent, rootIdx))
            {
                subtree.Add(ent);
            }
        }

        var listPos = new Dictionary<int, int>();
        for (int i = 0; i < subtree.Count; i++)
            listPos[subtree[i]] = i;

        foreach (int idx in subtree)
        {
            ref var t = ref transforms.Get(idx);

            var e = new EntityData
            {
                Name = names.Has(idx) ? names.Get(idx).Value : $"Entity {idx}",
                Position = [t.Position.X, t.Position.Y, t.Position.Z],
                Rotation = [t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W],
                Scale = [t.Scale.X, t.Scale.Y, t.Scale.Z],
                Parent = t.Parent >= 0 && listPos.TryGetValue(t.Parent, out int p) ? p : -1
            };

            if (prefabLinks != null && prefabLinks.Has(idx))
            {
                var pl = prefabLinks.Get(idx);
                e.PrefabPath = pl.PrefabPath;
                e.PrefabOverrides = pl.Overrides;
            }

            if (idx == rootIdx)
            {
                e.Parent = -1;
            }

            if (renderers.Has(idx))
            {
                ref var r = ref renderers.Get(idx);
                e.MeshRenderer = new MeshRendererData
                {
                    ModelPath = r.ModelHandle >= 0 ? assets.GetRelativePath(r.ModelHandle) : null,
                    Tint = [r.Tint.X, r.Tint.Y, r.Tint.Z],
                    Visible = r.Visible,
                    AlbedoTexturePath = r.AlbedoTexturePath,
                    NormalMapPath = r.NormalMapPath,
                    MetallicRoughnessMapPath = r.MetallicRoughnessMapPath,
                    MetallicFactor = r.MetallicFactor,
                    RoughnessFactor = r.RoughnessFactor
                };
            }

            if (emitters.Has(idx))
            {
                ref var pe = ref emitters.Get(idx);
                e.ParticleEmitter = new ParticleEmitterData
                {
                    Active = pe.Active,
                    Type = pe.Type,
                    SpawnRate = pe.SpawnRate,
                    ColorStart = [pe.ColorStart.X, pe.ColorStart.Y, pe.ColorStart.Z],
                    ColorEnd = [pe.ColorEnd.X, pe.ColorEnd.Y, pe.ColorEnd.Z],
                    SizeStart = pe.SizeStart,
                    SizeMiddle = pe.SizeMiddle,
                    SizeEnd = pe.SizeEnd,
                    Lifetime = pe.Lifetime,
                    Gravity = [pe.Gravity.X, pe.Gravity.Y, pe.Gravity.Z],
                    VelocityMin = [pe.VelocityMin.X, pe.VelocityMin.Y, pe.VelocityMin.Z],
                    VelocityMax = [pe.VelocityMax.X, pe.VelocityMax.Y, pe.VelocityMax.Z],
                    TexturePath = pe.TexturePath
                };
            }

            if (audioSources.Has(idx))
            {
                ref var src = ref audioSources.Get(idx);
                e.AudioSource = new AudioSourceData
                {
                    Active = src.Active,
                    ClipPath = src.ClipPath,
                    Volume = src.Volume,
                    Pitch = src.Pitch,
                    Loop = src.Loop,
                    Spatial3D = src.Spatial3D
                };
            }

            if (behaviors.Has(idx))
            {
                ref var b = ref behaviors.Get(idx);
                e.Behavior = new BehaviorData
                {
                    Active = b.Active,
                    Type = b.Type,
                    Axis = [b.Axis.X, b.Axis.Y, b.Axis.Z],
                    Speed = b.Speed,
                    Range = b.Range,
                    Frequency = b.Frequency
                };
            }

            if (triggers.Has(idx))
            {
                ref var tr = ref triggers.Get(idx);
                e.Trigger = new TriggerData
                {
                    Active = tr.Active,
                    Size = [tr.Size.X, tr.Size.Y, tr.Size.Z],
                    TargetEntity = tr.TargetEntity >= 0 && listPos.TryGetValue(tr.TargetEntity, out int targetP) ? targetP : -1
                };
            }

            if (actions.Has(idx))
            {
                ref var act = ref actions.Get(idx);
                e.Action = new ActionData
                {
                    Active = act.Active,
                    ActionType = act.ActionType,
                    ActionParam = act.ActionParam
                };
            }

            if (lights.Has(idx))
            {
                ref var l = ref lights.Get(idx);
                e.Light = new LightData
                {
                    Active = l.Active,
                    Color = [l.Color.X, l.Color.Y, l.Color.Z],
                    Radius = l.Radius,
                    Intensity = l.Intensity
                };
            }

            scene.Entities.Add(e);
        }

        scene.Lighting = null;
        return scene;
    }

    public static int ApplyPrefabData(SceneData prefab, Vector3 spawnPosition, World world, Store<Transform> transforms, Store<MeshRenderer> renderers,
        Store<Name> names, Store<ParticleEmitter> emitters, Store<AudioSourceComponent> audioSources, Store<BehaviorComponent> behaviors,
        Store<TriggerComponent> triggers, Store<ActionComponent> actions, Store<LightComponent> lights, AssetManager assets,
        string prefabPath, Store<PrefabLink> prefabLinks)
    {
        if (prefab.Entities.Count == 0) return -1;

        var created = new int[prefab.Entities.Count];
        for (int i = 0; i < prefab.Entities.Count; i++)
            created[i] = world.Create().Index;

        for (int i = 0; i < prefab.Entities.Count; i++)
        {
            var e = prefab.Entities[i];
            int idx = created[i];

            var t = Transform.Identity;
            if (e.Position is { Length: >= 3 })
                t.Position = new Vector3(e.Position[0], e.Position[1], e.Position[2]);
            if (e.Rotation is { Length: >= 4 })
                t.Rotation = new Quaternion(e.Rotation[0], e.Rotation[1], e.Rotation[2], e.Rotation[3]);
            if (e.Scale is { Length: >= 3 })
                t.Scale = new Vector3(e.Scale[0], e.Scale[1], e.Scale[2]);
            t.Parent = (e.Parent >= 0 && e.Parent < created.Length) ? created[e.Parent] : -1;

            if (i == 0)
            {
                t.Position = spawnPosition;
            }

            transforms.Add(idx, t);
            names.Add(idx, new Name { Value = e.Name });

            if (e.MeshRenderer is { } mr)
            {
                int handle = mr.ModelPath is null
                    ? -1
                    : assets.LoadModel(Path.Combine(AssetManager.AssetsRoot, mr.ModelPath));

                renderers.Add(idx, new MeshRenderer
                {
                    ModelHandle = handle,
                    Tint = mr.Tint is { Length: >= 3 } ? new Vector3(mr.Tint[0], mr.Tint[1], mr.Tint[2]) : Vector3.One,
                    Visible = mr.Visible,
                    AlbedoTexturePath = mr.AlbedoTexturePath ?? "",
                    NormalMapPath = mr.NormalMapPath ?? "",
                    MetallicRoughnessMapPath = mr.MetallicRoughnessMapPath ?? "",
                    MetallicFactor = mr.MetallicFactor,
                    RoughnessFactor = mr.RoughnessFactor
                });
            }

            if (e.ParticleEmitter is { } pe)
            {
                emitters.Add(idx, new ParticleEmitter
                {
                    Active = pe.Active,
                    Type = pe.Type,
                    SpawnRate = pe.SpawnRate,
                    ColorStart = pe.ColorStart is { Length: >= 3 } ? new Vector3(pe.ColorStart[0], pe.ColorStart[1], pe.ColorStart[2]) : Vector3.One,
                    ColorEnd = pe.ColorEnd is { Length: >= 3 } ? new Vector3(pe.ColorEnd[0], pe.ColorEnd[1], pe.ColorEnd[2]) : Vector3.One,
                    SizeStart = pe.SizeStart,
                    SizeMiddle = pe.SizeMiddle,
                    SizeEnd = pe.SizeEnd,
                    Lifetime = pe.Lifetime,
                    Gravity = pe.Gravity is { Length: >= 3 } ? new Vector3(pe.Gravity[0], pe.Gravity[1], pe.Gravity[2]) : Vector3.Zero,
                    VelocityMin = pe.VelocityMin is { Length: >= 3 } ? new Vector3(pe.VelocityMin[0], pe.VelocityMin[1], pe.VelocityMin[2]) : Vector3.Zero,
                    VelocityMax = pe.VelocityMax is { Length: >= 3 } ? new Vector3(pe.VelocityMax[0], pe.VelocityMax[1], pe.VelocityMax[2]) : Vector3.Zero,
                    TexturePath = pe.TexturePath ?? ""
                });
            }

            if (e.AudioSource is { } src)
            {
                audioSources.Add(idx, new AudioSourceComponent
                {
                    Active = src.Active,
                    ClipPath = src.ClipPath ?? "",
                    Volume = src.Volume,
                    Pitch = src.Pitch,
                    Loop = src.Loop,
                    Spatial3D = src.Spatial3D,
                    IsPlaying = false
                });
            }

            if (e.Behavior is { } b)
            {
                behaviors.Add(idx, new BehaviorComponent
                {
                    Active = b.Active,
                    Type = b.Type,
                    Axis = b.Axis is { Length: >= 3 } ? new Vector3(b.Axis[0], b.Axis[1], b.Axis[2]) : Vector3.UnitY,
                    Speed = b.Speed,
                    Range = b.Range,
                    Frequency = b.Frequency,
                    Initialized = false
                });
            }

            if (e.Trigger is { } tr)
            {
                triggers.Add(idx, new TriggerComponent
                {
                    Active = tr.Active,
                    Size = new Vector3(tr.Size[0], tr.Size[1], tr.Size[2]),
                    TargetEntity = tr.TargetEntity, // Bude renamovano nize
                    WasTriggered = false
                });
            }

            if (e.Action is { } act)
            {
                actions.Add(idx, new ActionComponent
                {
                    Active = act.Active,
                    ActionType = act.ActionType,
                    ActionParam = act.ActionParam ?? "",
                    TriggeredState = false
                });
            }

            if (e.Light is { } l)
            {
                lights.Add(idx, new LightComponent
                {
                    Active = l.Active,
                    Color = new Vector3(l.Color[0], l.Color[1], l.Color[2]),
                    Radius = l.Radius,
                    Intensity = l.Intensity
                });
            }

            if (prefabLinks != null && !string.IsNullOrEmpty(e.PrefabPath))
            {
                prefabLinks.Add(idx, new PrefabLink { PrefabPath = e.PrefabPath, Overrides = e.PrefabOverrides ?? "" });
            }
            else if (i == 0 && prefabLinks != null && !string.IsNullOrEmpty(prefabPath))
            {
                prefabLinks.Add(idx, new PrefabLink { PrefabPath = prefabPath, Overrides = "" });
            }
        }

        // Remap TargetEntity pro spoustec / akce v prefabu
        for (int i = 0; i < prefab.Entities.Count; i++)
        {
            var e = prefab.Entities[i];
            int idx = created[i];
            if (e.Trigger is { } tr && tr.TargetEntity >= 0 && tr.TargetEntity < created.Length)
            {
                ref var triggerComp = ref triggers.Get(idx);
                triggerComp.TargetEntity = created[tr.TargetEntity];
            }
        }

        return created[0];
    }

    public static void Save(string path, Store<Transform> transforms, Store<MeshRenderer> renderers,
        Store<Name> names, Store<ParticleEmitter> emitters, Store<AudioSourceComponent> audioSources, Store<BehaviorComponent> behaviors,
        Store<TriggerComponent> triggers, Store<ActionComponent> actions, Store<LightComponent> lights, AssetManager assets, LightingShader lighting, PostProcessing postProcessor,
        Store<PrefabLink> prefabLinks)
    {
        var scene = CreateSceneData(transforms, renderers, names, emitters, audioSources, behaviors, triggers, actions, lights, assets, lighting, postProcessor, prefabLinks);
        File.WriteAllText(path, JsonSerializer.Serialize(scene, SceneJsonContext.Default.SceneData));
    }

    public static void Load(string path, World world, Store<Transform> transforms, Store<MeshRenderer> renderers,
        Store<Name> names, Store<ParticleEmitter> emitters, Store<AudioSourceComponent> audioSources, Store<BehaviorComponent> behaviors,
        Store<TriggerComponent> triggers, Store<ActionComponent> actions, Store<LightComponent> lights, AssetManager assets, LightingShader lighting, PostProcessing postProcessor,
        Store<PrefabLink> prefabLinks)
    {
        var scene = JsonSerializer.Deserialize(File.ReadAllText(path), SceneJsonContext.Default.SceneData);
        if (scene is null) return;
        ApplySceneData(scene, world, transforms, renderers, names, emitters, audioSources, behaviors, triggers, actions, lights, assets, lighting, postProcessor, prefabLinks);
    }

    public static void PropagatePrefabs(World world, Store<Transform> transforms, Store<MeshRenderer> renderers,
        Store<Name> names, Store<ParticleEmitter> emitters, Store<AudioSourceComponent> audioSources, Store<BehaviorComponent> behaviors,
        Store<TriggerComponent> triggers, Store<ActionComponent> actions, Store<LightComponent> lights, AssetManager assets,
        Store<PrefabLink> prefabLinks)
    {
        var roots = prefabLinks.Entities;
        var rootsCopy = new int[roots.Length];
        roots.CopyTo(rootsCopy);

        foreach (int rootIdx in rootsCopy)
        {
            var pl = prefabLinks.Get(rootIdx);
            if (string.IsNullOrEmpty(pl.PrefabPath)) continue;

            string fullPath = Path.Combine(AppContext.BaseDirectory, "assets", pl.PrefabPath);
            if (!File.Exists(fullPath)) continue;

            try
            {
                var prefabJson = File.ReadAllText(fullPath);
                var prefab = JsonSerializer.Deserialize(prefabJson, SceneJsonContext.Default.SceneData);
                if (prefab == null || prefab.Entities.Count == 0) continue;

                var sceneSubtree = new List<int>();
                GetDfsSubtree(transforms, rootIdx, sceneSubtree);

                var prefabSubtree = new List<int>();
                GetDfsSubtreeData(prefab.Entities, 0, prefabSubtree);

                int count = Math.Min(sceneSubtree.Count, prefabSubtree.Count);

                var overrides = new Dictionary<string, List<string>>();
                if (!string.IsNullOrEmpty(pl.Overrides))
                {
                    try
                    {
                        overrides = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(pl.Overrides);
                    }
                    catch { }
                }
                if (overrides == null) overrides = new Dictionary<string, List<string>>();

                for (int i = 0; i < count; i++)
                {
                    int sEnt = sceneSubtree[i];
                    int pEntPos = prefabSubtree[i];
                    var pEnt = prefab.Entities[pEntPos];
                    string key = i.ToString();

                    var entOverrides = overrides.TryGetValue(key, out var list) ? list : new List<string>();

                    if (transforms.Has(sEnt))
                    {
                        ref var t = ref transforms.Get(sEnt);
                        if (i > 0)
                        {
                            if (!entOverrides.Contains("Transform.Position") && pEnt.Position is { Length: >= 3 })
                                t.Position = new Vector3(pEnt.Position[0], pEnt.Position[1], pEnt.Position[2]);
                            if (!entOverrides.Contains("Transform.Rotation") && pEnt.Rotation is { Length: >= 4 })
                                t.Rotation = new Quaternion(pEnt.Rotation[0], pEnt.Rotation[1], pEnt.Rotation[2], pEnt.Rotation[3]);
                            if (!entOverrides.Contains("Transform.Scale") && pEnt.Scale is { Length: >= 3 })
                                t.Scale = new Vector3(pEnt.Scale[0], pEnt.Scale[1], pEnt.Scale[2]);
                        }
                    }

                    if (names.Has(sEnt))
                    {
                        if (!entOverrides.Contains("Name"))
                        {
                            ref var n = ref names.Get(sEnt);
                            n.Value = pEnt.Name;
                        }
                    }

                    if (pEnt.MeshRenderer != null)
                    {
                        if (!renderers.Has(sEnt))
                        {
                            renderers.Add(sEnt, new MeshRenderer { ModelHandle = -1, Tint = Vector3.One, Visible = true });
                        }
                        ref var r = ref renderers.Get(sEnt);

                        if (!entOverrides.Contains("MeshRenderer.Tint") && pEnt.MeshRenderer.Tint is { Length: >= 3 })
                            r.Tint = new Vector3(pEnt.MeshRenderer.Tint[0], pEnt.MeshRenderer.Tint[1], pEnt.MeshRenderer.Tint[2]);

                        if (!entOverrides.Contains("MeshRenderer.Visible"))
                            r.Visible = pEnt.MeshRenderer.Visible;

                        if (!entOverrides.Contains("MeshRenderer.AlbedoTexturePath"))
                            r.AlbedoTexturePath = pEnt.MeshRenderer.AlbedoTexturePath ?? "";

                        if (!entOverrides.Contains("MeshRenderer.NormalMapPath"))
                            r.NormalMapPath = pEnt.MeshRenderer.NormalMapPath ?? "";

                        if (!entOverrides.Contains("MeshRenderer.MetallicRoughnessMapPath"))
                            r.MetallicRoughnessMapPath = pEnt.MeshRenderer.MetallicRoughnessMapPath ?? "";

                        if (!entOverrides.Contains("MeshRenderer.MetallicFactor"))
                            r.MetallicFactor = pEnt.MeshRenderer.MetallicFactor;

                        if (!entOverrides.Contains("MeshRenderer.RoughnessFactor"))
                            r.RoughnessFactor = pEnt.MeshRenderer.RoughnessFactor;

                        if (!entOverrides.Contains("MeshRenderer.ModelPath"))
                        {
                            int handle = pEnt.MeshRenderer.ModelPath == null
                                ? -1
                                : assets.LoadModel(Path.Combine(AssetManager.AssetsRoot, pEnt.MeshRenderer.ModelPath));
                            if (r.ModelHandle >= 0 && r.ModelHandle != handle)
                            {
                                assets.Release(r.ModelHandle);
                            }
                            r.ModelHandle = handle;
                        }
                    }
                    else if (renderers.Has(sEnt))
                    {
                        if (!entOverrides.Contains("MeshRenderer.Removed"))
                        {
                            ref var r = ref renderers.Get(sEnt);
                            if (r.ModelHandle >= 0) assets.Release(r.ModelHandle);
                            renderers.RemoveAt(sEnt);
                        }
                    }

                    if (pEnt.Light != null)
                    {
                        if (!lights.Has(sEnt))
                        {
                            lights.Add(sEnt, LightComponent.Default);
                        }
                        ref var l = ref lights.Get(sEnt);

                        if (!entOverrides.Contains("Light.Active"))
                            l.Active = pEnt.Light.Active;

                        if (!entOverrides.Contains("Light.Color") && pEnt.Light.Color is { Length: >= 3 })
                            l.Color = new Vector3(pEnt.Light.Color[0], pEnt.Light.Color[1], pEnt.Light.Color[2]);

                        if (!entOverrides.Contains("Light.Radius"))
                            l.Radius = pEnt.Light.Radius;

                        if (!entOverrides.Contains("Light.Intensity"))
                            l.Intensity = pEnt.Light.Intensity;
                    }
                    else if (lights.Has(sEnt))
                    {
                        if (!entOverrides.Contains("Light.Removed"))
                        {
                            lights.RemoveAt(sEnt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chyba propagace prefabu: {ex.Message}");
            }
        }
    }

    public static void GetDfsSubtree(Store<Transform> transforms, int current, List<int> result)
    {
        result.Add(current);
        var children = new List<int>();
        var entities = transforms.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            int e = entities[i];
            if (e != current && transforms.Get(e).Parent == current)
            {
                children.Add(e);
            }
        }
        children.Sort();
        foreach (var child in children)
        {
            GetDfsSubtree(transforms, child, result);
        }
    }

    public static void GetDfsSubtreeData(List<EntityData> entities, int currentPos, List<int> result)
    {
        result.Add(currentPos);
        var children = new List<int>();
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i].Parent == currentPos)
            {
                children.Add(i);
            }
        }
        children.Sort();
        foreach (var child in children)
        {
            GetDfsSubtreeData(entities, child, result);
        }
    }

    public static bool IsPartOfPrefab(Store<Transform> transforms, Store<PrefabLink> prefabLinks, int entity, out int prefabRoot)
    {
        prefabRoot = -1;
        if (prefabLinks == null) return false;
        int current = entity;
        while (current >= 0)
        {
            if (prefabLinks.Has(current))
            {
                prefabRoot = current;
                return true;
            }
            if (!transforms.Has(current)) break;
            current = transforms.Get(current).Parent;
        }
        return false;
    }
}
