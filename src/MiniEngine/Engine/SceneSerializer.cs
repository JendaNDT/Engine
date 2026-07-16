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
    public MeshRendererData? MeshRenderer { get; set; }
    public ParticleEmitterData? ParticleEmitter { get; set; }
    public AudioSourceData? AudioSource { get; set; }
    public BehaviorData? Behavior { get; set; }
}

public sealed class MeshRendererData
{
    public string? ModelPath { get; set; }                  // null = placeholder krychle
    public float[] Tint { get; set; } = [1, 1, 1];
    public bool Visible { get; set; } = true;
    public string? AlbedoTexturePath { get; set; }
}

public sealed class ParticleEmitterData
{
    public bool Active { get; set; }
    public int Type { get; set; }
    public float SpawnRate { get; set; }
    public float[] ColorStart { get; set; } = [1, 0.5f, 0.1f];
    public float[] ColorEnd { get; set; } = [0.2f, 0.2f, 0.2f];
    public float SizeStart { get; set; } = 0.2f;
    public float SizeEnd { get; set; } = 0.05f;
    public float Lifetime { get; set; } = 1.5f;
    public float[] Gravity { get; set; } = [0, 1, 0];
    public float[] VelocityMin { get; set; } = [-0.5f, 1f, -0.5f];
    public float[] VelocityMax { get; set; } = [0.5f, 2f, 0.5f];
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

/// <summary>
/// Source-generated JSON kontext - POVINNE kvuli NativeAOT.
/// Reflexni serializace by v AOT publish spadla (viz DEEP_RESEARCH 2.3).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SceneData))]
[JsonSerializable(typeof(ParticleEmitterData))]
[JsonSerializable(typeof(AudioSourceData))]
[JsonSerializable(typeof(BehaviorData))]
internal partial class SceneJsonContext : JsonSerializerContext { }

public static class SceneSerializer
{
    public static void Save(string path, Store<Transform> transforms, Store<MeshRenderer> renderers,
        Store<Name> names, Store<ParticleEmitter> emitters, Store<AudioSourceComponent> audioSources, Store<BehaviorComponent> behaviors, AssetManager assets, LightingShader lighting)
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

            if (renderers.Has(idx))
            {
                ref var r = ref renderers.Get(idx);
                e.MeshRenderer = new MeshRendererData
                {
                    ModelPath = r.ModelHandle >= 0 ? assets.GetRelativePath(r.ModelHandle) : null,
                    Tint = [r.Tint.X, r.Tint.Y, r.Tint.Z],
                    Visible = r.Visible,
                    AlbedoTexturePath = r.AlbedoTexturePath
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
                    SizeEnd = pe.SizeEnd,
                    Lifetime = pe.Lifetime,
                    Gravity = [pe.Gravity.X, pe.Gravity.Y, pe.Gravity.Z],
                    VelocityMin = [pe.VelocityMin.X, pe.VelocityMin.Y, pe.VelocityMin.Z],
                    VelocityMax = [pe.VelocityMax.X, pe.VelocityMax.Y, pe.VelocityMax.Z]
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

        File.WriteAllText(path, JsonSerializer.Serialize(scene, SceneJsonContext.Default.SceneData));
    }

    public static void Load(string path, World world, Store<Transform> transforms, Store<MeshRenderer> renderers,
        Store<Name> names, Store<ParticleEmitter> emitters, Store<AudioSourceComponent> audioSources, Store<BehaviorComponent> behaviors, AssetManager assets, LightingShader lighting)
    {
        var scene = JsonSerializer.Deserialize(File.ReadAllText(path), SceneJsonContext.Default.SceneData);
        if (scene is null) return;

        if (scene.Lighting is { } l)
        {
            lighting.SunDirection = new Vector3(l.SunDirection[0], l.SunDirection[1], l.SunDirection[2]);
            lighting.SunColor = new Vector3(l.SunColor[0], l.SunColor[1], l.SunColor[2]);
            lighting.SunIntensity = l.SunIntensity;
            lighting.Ambient = new Vector3(l.Ambient[0], l.Ambient[1], l.Ambient[2]);
            lighting.SpecStrength = l.SpecStrength;
        }

        // 1) Smazat soucasnou scenu = vsechno, co ma Transform.
        //    Model handly se uvolni (refcount), GPU pamet nezustane viset.
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
            world.Destroy(world.EntityFromIndex(idx));
        }

        // 2) Nejdriv vytvorit vsechny entity (kvuli remapu Parent), pak plnit komponenty.
        var created = new int[scene.Entities.Count];
        for (int i = 0; i < scene.Entities.Count; i++)
            created[i] = world.Create().Index;

        for (int i = 0; i < scene.Entities.Count; i++)
        {
            var e = scene.Entities[i];
            int idx = created[i];

            var t = Transform.Identity;
            t.Position = new Vector3(e.Position[0], e.Position[1], e.Position[2]);
            t.Rotation = new Quaternion(e.Rotation[0], e.Rotation[1], e.Rotation[2], e.Rotation[3]);
            t.Scale = new Vector3(e.Scale[0], e.Scale[1], e.Scale[2]);
            t.Parent = e.Parent >= 0 ? created[e.Parent] : -1;
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
                    Tint = new Vector3(mr.Tint[0], mr.Tint[1], mr.Tint[2]),
                    Visible = mr.Visible,
                    AlbedoTexturePath = mr.AlbedoTexturePath ?? ""
                });
            }

            if (e.ParticleEmitter is { } pe)
            {
                emitters.Add(idx, new ParticleEmitter
                {
                    Active = pe.Active,
                    Type = pe.Type,
                    SpawnRate = pe.SpawnRate,
                    ColorStart = new Vector3(pe.ColorStart[0], pe.ColorStart[1], pe.ColorStart[2]),
                    ColorEnd = new Vector3(pe.ColorEnd[0], pe.ColorEnd[1], pe.ColorEnd[2]),
                    SizeStart = pe.SizeStart,
                    SizeEnd = pe.SizeEnd,
                    Lifetime = pe.Lifetime,
                    Gravity = new Vector3(pe.Gravity[0], pe.Gravity[1], pe.Gravity[2]),
                    VelocityMin = new Vector3(pe.VelocityMin[0], pe.VelocityMin[1], pe.VelocityMin[2]),
                    VelocityMax = new Vector3(pe.VelocityMax[0], pe.VelocityMax[1], pe.VelocityMax[2])
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
        }
    }
}
