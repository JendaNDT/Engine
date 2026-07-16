using System.Numerics;

namespace MiniEngine.Core;

/// <summary>
/// Kvaternion je zdroj pravdy pro rotaci. Eulery drz jen jako docasny stav
/// v inspektoru, jinak ti hodnoty pri prechodu pres 90 stupnu preskoci.
/// </summary>
public struct Transform
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    /// <summary>Index rodicovske entity, -1 = root.</summary>
    public int Parent;

    /// <summary>Cache world matice. Prepocitava TransformSystem.</summary>
    public Matrix4x4 World;

    public static Transform Identity => new()
    {
        Position = Vector3.Zero,
        Rotation = Quaternion.Identity,
        Scale = Vector3.One,
        Parent = -1,
        World = Matrix4x4.Identity
    };

    public readonly Matrix4x4 LocalMatrix =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) *
        Matrix4x4.CreateTranslation(Position);
}

/// <summary>Odkazuje na model handlem (int), NIKDY ne kopii raylib struktury Model.</summary>
public struct MeshRenderer
{
    public int ModelHandle;
    public Vector3 Tint;     // 0..1, prevede se na Color az pri kresleni
    public bool Visible;
    public string AlbedoTexturePath;
}

/// <summary>Placeholder do doby, nez pripojis BepuPhysics.</summary>
public struct RigidBodyRef
{
    public int BodyHandle;   // BepuPhysics.BodyHandle.Value
    public bool IsKinematic;
    public Vector3 CenterOffset;
}

/// <summary>
/// Jmeno entity pro editor. String se alokuje jen pri vytvoreni/prejmenovani,
/// ne per frame - v hot pathu enginu se na nej nesaha.
/// </summary>
public struct Name
{
    public string Value;
}

public struct ParticleEmitter
{
    public bool Active;
    public int Type; // 0 = Fire, 1 = Smoke, 2 = Spark
    public float SpawnRate; // částic za sekundu
    public float SpawnTimer;
    public Vector3 ColorStart;
    public Vector3 ColorEnd;
    public float SizeStart;
    public float SizeEnd;
    public float Lifetime;
    public Vector3 Gravity;
    public Vector3 VelocityMin;
    public Vector3 VelocityMax;

    public static ParticleEmitter Default => new()
    {
        Active = false,
        Type = 0,
        SpawnRate = 30f,
        SpawnTimer = 0f,
        ColorStart = new Vector3(1.0f, 0.5f, 0.1f),
        ColorEnd = new Vector3(0.2f, 0.2f, 0.2f),
        SizeStart = 0.2f,
        SizeEnd = 0.05f,
        Lifetime = 1.5f,
        Gravity = new Vector3(0f, 1f, 0f),
        VelocityMin = new Vector3(-0.5f, 1f, -0.5f),
        VelocityMax = new Vector3(0.5f, 2f, 0.5f)
    };
}

public struct AudioSourceComponent
{
    public bool Active;
    public string ClipPath;
    public float Volume;
    public float Pitch;
    public bool Loop;
    public bool Spatial3D;
    public bool IsPlaying;

    public static AudioSourceComponent Default => new()
    {
        Active = false,
        ClipPath = "",
        Volume = 1f,
        Pitch = 1f,
        Loop = false,
        Spatial3D = true,
        IsPlaying = false
    };
}

/// <summary>
/// Prepocet world matic. Rodic se dopocitava rekurzivne pres "stamp" pole,
/// takze NEZALEZI na poradi v dense poli (driv musel rodic lezet pred ditetem -
/// s reparentovanim by to neslo udrzet). Instance drzi stav, uz to neni static.
/// Zadne alokace za frame - stamp pole roste jen s nejvyssim indexem entity.
/// </summary>
public sealed class TransformSystem
{
    // _stamp[i] ==  _frame : world matice entity i je letos spocitana
    // _stamp[i] == -_frame : prave se pocita (navsteva behem rekurze = CYKLUS)
    // cokoli jineho        : jeste nespocitana
    private int[] _stamp = [];
    private int _frame;

    public void UpdateWorldMatrices(Store<Transform> transforms)
    {
        var entities = transforms.Entities;

        int maxIndex = -1;
        for (int i = 0; i < entities.Length; i++)
            if (entities[i] > maxIndex) maxIndex = entities[i];

        if (_stamp.Length <= maxIndex)
            Array.Resize(ref _stamp, Math.Max(64, Math.Max(maxIndex + 1, _stamp.Length * 2)));

        _frame++;
        for (int i = 0; i < entities.Length; i++)
            Compute(transforms, entities[i]);
    }

    private void Compute(Store<Transform> transforms, int entityIndex)
    {
        if (_stamp[entityIndex] == _frame) return;   // uz hotovo (napr. jako rodic nekoho driv)
        _stamp[entityIndex] = -_frame;               // "pocita se" - detekce cyklu

        ref var t = ref transforms.Get(entityIndex);
        var local = t.LocalMatrix;

        // Parent je index ENTITY, ne dense slotu - proto sparse Get().
        // Rodic "prave pocitany" znamena cyklus (rozbita scena) - radsi world = local
        // nez nekonecna rekurze.
        if (t.Parent >= 0 && t.Parent < _stamp.Length && transforms.Has(t.Parent) &&
            _stamp[t.Parent] != -_frame)
        {
            Compute(transforms, t.Parent);
            t.World = local * transforms.Get(t.Parent).World;
        }
        else
        {
            t.World = local;
        }

        _stamp[entityIndex] = _frame;
    }
}
