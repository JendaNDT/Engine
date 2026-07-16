using System.Numerics;

namespace MiniEngine.Core;

/// <summary>
/// Prevody mezi world a lokalnim prostorem pro hierarchii transformu + reparenting.
/// Cista matika bez raylibu - testuje se v konzoli na skutecnem kodu.
///
/// KONVENCE (overeno testem 16. 7. 2026): u kvaternionu System.Numerics
/// 'a * b' aplikuje NEJDRIV b, pak a. World rotace ditete = parentQ * localQ.
/// U Matrix4x4 je to naopak: world matice = local * parentWorld.
/// </summary>
public static class TransformHierarchy
{
    private const int MaxDepth = 256;   // pojistka proti cyklu z rozbite sceny

    /// <summary>World rotace = soucin lokalnich rotaci po retezu rodicu.</summary>
    public static Quaternion WorldRotation(Store<Transform> transforms, int entityIndex)
    {
        var q = Quaternion.Identity;
        int idx = entityIndex;
        int guard = 0;

        while (idx >= 0 && transforms.Has(idx) && guard++ < MaxDepth)
        {
            ref var t = ref transforms.Get(idx);
            q = t.Rotation * q;   // rodic se aplikuje PO diteti -> nasobi se zleva
            idx = t.Parent;
        }

        return Quaternion.Normalize(q);
    }

    /// <summary>
    /// Priblizne world meritko - delky bazovych vektoru world matice.
    /// U nerovnomerneho meritka rodice kombinovaneho s rotaci vznika zkoseni,
    /// ktere TRS neumi popsat - tohle je stejny kompromis, jaky dela Unity.
    /// </summary>
    public static Vector3 WorldScale(in Matrix4x4 world) =>
        new(new Vector3(world.M11, world.M12, world.M13).Length(),
            new Vector3(world.M21, world.M22, world.M23).Length(),
            new Vector3(world.M31, world.M32, world.M33).Length());

    /// <summary>Nastavi LOKALNI pozici tak, aby world pozice vysla presne 'worldPos'.</summary>
    public static void SetWorldPosition(Store<Transform> transforms, int entityIndex, Vector3 worldPos)
    {
        ref var t = ref transforms.Get(entityIndex);

        if (t.Parent >= 0 && transforms.Has(t.Parent) &&
            Matrix4x4.Invert(transforms.Get(t.Parent).World, out var invParent))
        {
            t.Position = Vector3.Transform(worldPos, invParent);
        }
        else
        {
            t.Position = worldPos;
        }
    }

    /// <summary>Nastavi LOKALNI rotaci tak, aby world rotace vysla 'worldRot'.</summary>
    public static void SetWorldRotation(Store<Transform> transforms, int entityIndex, Quaternion worldRot)
    {
        ref var t = ref transforms.Get(entityIndex);

        if (t.Parent >= 0 && transforms.Has(t.Parent))
        {
            var parentRot = WorldRotation(transforms, t.Parent);
            // world = parent * local  ->  local = inv(parent) * world
            t.Rotation = Quaternion.Normalize(Quaternion.Inverse(parentRot) * worldRot);
        }
        else
        {
            t.Rotation = Quaternion.Normalize(worldRot);
        }
    }

    /// <summary>Je 'candidate' entita 'ancestor' nebo jeji potomek? (Zakaz cyklu pri reparentovani.)</summary>
    public static bool IsDescendantOf(Store<Transform> transforms, int candidate, int ancestor)
    {
        int idx = candidate;
        int guard = 0;

        while (idx >= 0 && transforms.Has(idx) && guard++ < MaxDepth)
        {
            if (idx == ancestor) return true;
            idx = transforms.Get(idx).Parent;
        }

        return false;
    }

    /// <summary>
    /// Prevesi entitu pod noveho rodice (-1 = koren) a prepocita lokalni TRS tak,
    /// aby WORLD pozice, rotace i meritko zustaly na miste (objekt se ve scene nehne).
    /// Cyklus nebo neplatny cil = false a nic se nezmeni.
    /// PREDPOKLAD: world matice jsou cerstve (po poslednim UpdateWorldMatrices).
    /// </summary>
    public static bool Reparent(Store<Transform> transforms, int child, int newParent)
    {
        if (child < 0 || !transforms.Has(child)) return false;
        if (newParent == child) return false;
        if (newParent >= 0 &&
            (!transforms.Has(newParent) || IsDescendantOf(transforms, newParent, child)))
            return false;

        // world poza PRED prevesenim
        ref var t = ref transforms.Get(child);
        var worldPos = t.World.Translation;
        var worldRot = WorldRotation(transforms, child);
        var worldScale = WorldScale(t.World);

        t.Parent = newParent;

        // lokalni TRS "vydelene" novym rodicem
        SetWorldPosition(transforms, child, worldPos);
        SetWorldRotation(transforms, child, worldRot);

        if (newParent >= 0)
        {
            var parentScale = WorldScale(transforms.Get(newParent).World);
            t.Scale = new Vector3(
                worldScale.X / MathF.Max(1e-6f, parentScale.X),
                worldScale.Y / MathF.Max(1e-6f, parentScale.Y),
                worldScale.Z / MathF.Max(1e-6f, parentScale.Z));
        }
        else
        {
            t.Scale = worldScale;
        }

        return true;
    }
}
