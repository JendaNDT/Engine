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
}

public sealed class MeshRendererData
{
    public string? ModelPath { get; set; }                  // null = placeholder krychle
    public float[] Tint { get; set; } = [1, 1, 1];
    public bool Visible { get; set; } = true;
    public string? AlbedoTexturePath { get; set; }
}

/// <summary>
/// Source-generated JSON kontext - POVINNE kvuli NativeAOT.
/// Reflexni serializace by v AOT publish spadla (viz DEEP_RESEARCH 2.3).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SceneData))]
internal partial class SceneJsonContext : JsonSerializerContext { }

public static class SceneSerializer
{
    public static void Save(string path, Store<Transform> transforms, Store<MeshRenderer> renderers,
        Store<Name> names, AssetManager assets, LightingShader lighting)
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
        Store<Name> names, AssetManager assets, LightingShader lighting)
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
        }
    }
}
