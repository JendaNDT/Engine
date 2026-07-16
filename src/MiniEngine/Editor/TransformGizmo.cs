using System.Numerics;
using MiniEngine.Core;
using Raylib_cs;
// Raylib_cs ma vlastni typ 'Transform' - bez aliasu kompilace pada na CS0104.
using Transform = MiniEngine.Core.Transform;

namespace MiniEngine.Editor;

public enum GizmoMode { Translate, Rotate, Scale }

/// <summary>
/// Transform gizmo se tremi rezimy (prepinani klavesami 1/2/3 nebo tlacitky v toolbaru):
///  - Posun:    sipky po WORLD osach (X cervena, Y zelena, Z modra)
///  - Rotace:   kruznice kolem WORLD os, tazeni po kruznici = otaceni
///  - Meritko:  osy ve WORLD orientaci objektu (u ditete vcetne rotace rodicu;
///              scale se aplikuje na lokalni Scale) + stredova koule = uniformni scale
///
/// S hierarchii pracuje gizmo VZDY ve world prostoru (kotva = World.Translation,
/// rotace = retez rodicu) a zapis jde pres TransformHierarchy.SetWorld*, ktere
/// prepocitaji lokalni hodnoty. Pro entity bez rodice je to totez co driv.
///
/// Game vola gizmo ve dvou fazich:
///  1) UpdateInteraction() v ImGui fazi - Hovered/Origin viewportu jsou cerstvé.
///     Vraci true, kdyz si gizmo vzalo mys - picking entit se pak MUSI preskocit.
///  2) Draw() uvnitr BeginMode3D, az PO objektech - kresli se pres scenu
///     s vypnutym depth testem, aby nezmizelo uvnitr objektu.
///
/// Ctrl/Cmd pri tazeni = prichytavani (posun 0.5 jednotky, rotace 15 stupnu, meritko krok 0.25).
/// Tazeni se latchne do pusteni tlacitka, i kdyz kurzor vyjede z viewportu.
/// Zadne alokace za frame (staticka pole, jen structy) - Stats panel musi dal hlasit 0 B.
/// </summary>
public sealed class TransformGizmo
{
    private static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];

    private static readonly Color[] AxisColors =
    [
        new Color(225, 65, 60, 255),    // X
        new Color(95, 205, 80, 255),    // Y
        new Color(70, 130, 250, 255),   // Z
    ];

    private static readonly Color HighlightColor = new(255, 210, 40, 255);
    private static readonly Color UniformColor = new(200, 200, 210, 255);

    public GizmoMode Mode { get; private set; } = GizmoMode.Translate;

    /// <summary>Uchyt pod kurzorem: 0..2 = osa, 3 = stredova koule (uniformni meritko), -1 = nic.</summary>
    public int HoverAxis { get; private set; } = -1;

    /// <summary>Prave probiha tazeni.</summary>
    public bool Dragging => _dragAxis >= 0;

    private int _dragAxis = -1;

    // posun + meritko po ose
    private float _grabT;              // parametr na ose v okamziku chyceni
    private Vector3 _dragStartPos;     // WORLD pozice pri chyceni
    private Vector3 _dragStartScale;   // LOKALNI meritko pri chyceni

    // rotace
    private Quaternion _dragStartRot;  // WORLD rotace pri chyceni
    private Vector3 _grabDir;          // jednotkovy smer od stredu k chycenemu bodu na kruznici
    private Vector3 _dragCurrentDir;   // aktualni smer behem tazeni (jen pro kresleni)

    // uniformni meritko (stredova koule) - jede ze zmeny X souradnice mysi
    private float _grabMouseX;

    public void SetMode(GizmoMode mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        CancelDrag();
    }

    /// <summary>Tvrde ukonceni tazeni (zmena vyberu/rezimu, start fyziky, smazani entity).</summary>
    public void CancelDrag()
    {
        _dragAxis = -1;
        HoverAxis = -1;
    }

    /// <summary>
    /// Zpracuje mys nad gizmem podle aktivniho rezimu.
    /// </summary>
    /// <returns>true = gizmo si vzalo mys (hover+klik nebo bezici tazeni).</returns>
    public bool UpdateInteraction(Store<Transform> transforms, int entityIndex,
        Ray ray, Camera3D camera, bool canStartDrag) => Mode switch
    {
        GizmoMode.Translate => UpdateTranslate(transforms, entityIndex, ray, camera, canStartDrag),
        GizmoMode.Rotate => UpdateRotate(transforms, entityIndex, ray, camera, canStartDrag),
        _ => UpdateScale(transforms, entityIndex, ray, camera, canStartDrag),
    };

    // ------------------------------------------------------------------ POSUN

    private bool UpdateTranslate(Store<Transform> transforms, int entityIndex,
        Ray ray, Camera3D camera, bool canStartDrag)
    {
        Vector3 worldPos = transforms.Get(entityIndex).World.Translation;

        if (Dragging)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                float param = ClosestAxisParam(_dragStartPos, Axes[_dragAxis], ray, _grabT);
                var p = _dragStartPos + Axes[_dragAxis] * (param - _grabT);

                if (SnapKeyDown())
                {
                    p.X = MathF.Round(p.X * 2f) * 0.5f;
                    p.Y = MathF.Round(p.Y * 2f) * 0.5f;
                    p.Z = MathF.Round(p.Z * 2f) * 0.5f;
                }

                TransformHierarchy.SetWorldPosition(transforms, entityIndex, p);
            }
            else
            {
                _dragAxis = -1;   // pusteni tlacitka = konec tazeni
            }

            return true;
        }

        HoverAxis = canStartDrag ? PickArrow(worldPos, ray, camera) : -1;

        if (HoverAxis >= 0 && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _dragAxis = HoverAxis;
            _dragStartPos = worldPos;
            _grabT = ClosestAxisParam(worldPos, Axes[_dragAxis], ray, 0f);
            return true;
        }

        return false;
    }

    /// <summary>Sipky posunu jsou world-aligned, takze staci AABB kolem kazde z nich.</summary>
    private static int PickArrow(Vector3 position, Ray ray, Camera3D camera)
    {
        float s = GizmoScale(position, camera);
        var pad = new Vector3(0.14f * s);

        int best = -1;
        float bestDist = float.MaxValue;

        for (int a = 0; a < 3; a++)
        {
            var from = position + Axes[a] * (0.15f * s);
            var to = position + Axes[a] * (1.15f * s);

            var hit = Raylib.GetRayCollisionBox(ray,
                new BoundingBox(Vector3.Min(from, to) - pad, Vector3.Max(from, to) + pad));

            if (hit.Hit && hit.Distance < bestDist)
            {
                bestDist = hit.Distance;
                best = a;
            }
        }

        return best;
    }

    // ------------------------------------------------------------------ ROTACE

    private bool UpdateRotate(Store<Transform> transforms, int entityIndex,
        Ray ray, Camera3D camera, bool canStartDrag)
    {
        Vector3 worldPos = transforms.Get(entityIndex).World.Translation;
        float radius = RingRadius(worldPos, camera);

        if (Dragging)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                // Prusecik paprsku s rovinou kruznice; kdyz je pohled skoro v rovine,
                // uhel se ten frame proste nezmeni (zadne poskoceni).
                if (PlaneHitDir(worldPos, Axes[_dragAxis], ray, out var dir))
                {
                    _dragCurrentDir = dir;
                    float angle = SignedAngle(_grabDir, dir, Axes[_dragAxis]);

                    if (SnapKeyDown())
                    {
                        const float step = 15f * MathF.PI / 180f;
                        angle = MathF.Round(angle / step) * step;
                    }

                    // World-space delta = delta * start. POZOR na konvenci: u KVATERNIONU
                    // v System.Numerics 'a * b' aplikuje NEJDRIV b, pak a (overeno testem
                    // 16. 7. 2026) - je to PRESNE NAOPAK nez u Matrix4x4 (tam 'a * b' = napred a).
                    var worldRot = Quaternion.Normalize(
                        Quaternion.CreateFromAxisAngle(Axes[_dragAxis], angle) * _dragStartRot);
                    TransformHierarchy.SetWorldRotation(transforms, entityIndex, worldRot);
                }
            }
            else
            {
                _dragAxis = -1;
            }

            return true;
        }

        HoverAxis = canStartDrag ? PickRing(worldPos, radius, ray) : -1;

        if (HoverAxis >= 0 && Raylib.IsMouseButtonPressed(MouseButton.Left) &&
            PlaneHitDir(worldPos, Axes[HoverAxis], ray, out var grab))
        {
            _dragAxis = HoverAxis;
            _dragStartRot = TransformHierarchy.WorldRotation(transforms, entityIndex);
            _grabDir = grab;
            _dragCurrentDir = grab;
            return true;
        }

        return false;
    }

    /// <summary>Kruznice pod kurzorem: prusecik s rovinou + vzdalenost od stredu blizko polomeru.</summary>
    private static int PickRing(Vector3 center, float radius, Ray ray)
    {
        int best = -1;
        float bestRayT = float.MaxValue;

        for (int a = 0; a < 3; a++)
        {
            float denom = Vector3.Dot(ray.Direction, Axes[a]);
            if (MathF.Abs(denom) < 1e-4f) continue;   // rovina hranou ke kamere - netrefitelna

            float rayT = Vector3.Dot(center - ray.Position, Axes[a]) / denom;
            if (rayT <= 0f || rayT >= bestRayT) continue;

            float fromCenter = (ray.Position + ray.Direction * rayT - center).Length();
            if (MathF.Abs(fromCenter - radius) < radius * 0.12f)
            {
                bestRayT = rayT;
                best = a;
            }
        }

        return best;
    }

    /// <summary>Prusecik paprsku s rovinou (stred, normala) prevedeny na jednotkovy smer od stredu.</summary>
    private static bool PlaneHitDir(Vector3 center, Vector3 normal, Ray ray, out Vector3 dir)
    {
        dir = default;

        float denom = Vector3.Dot(ray.Direction, normal);
        if (MathF.Abs(denom) < 1e-4f) return false;

        float rayT = Vector3.Dot(center - ray.Position, normal) / denom;
        if (rayT <= 0f) return false;

        var v = ray.Position + ray.Direction * rayT - center;
        float len = v.Length();
        if (len < 1e-5f) return false;

        dir = v / len;
        return true;
    }

    /// <summary>Orientovany uhel od 'from' k 'to' kolem 'axis' (radiany, -pi..pi).</summary>
    private static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis) =>
        MathF.Atan2(Vector3.Dot(Vector3.Cross(from, to), axis), Vector3.Dot(from, to));

    // ------------------------------------------------------------------ MERITKO

    private bool UpdateScale(Store<Transform> transforms, int entityIndex,
        Ray ray, Camera3D camera, bool canStartDrag)
    {
        ref var t = ref transforms.Get(entityIndex);
        Vector3 worldPos = t.World.Translation;
        float s = GizmoScale(worldPos, camera);

        if (Dragging)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                float factor;

                if (_dragAxis == 3)
                {
                    // Uniformni: tazeni doprava zvetsuje, doleva zmensuje.
                    factor = 1f + (Raylib.GetMousePosition().X - _grabMouseX) * 0.005f;
                }
                else
                {
                    // Po ose: pomer aktualniho a chytaciho parametru na ose
                    // ve WORLD orientaci objektu (vcetne rotace rodicu).
                    var dir = WorldAxis(transforms, entityIndex, _dragAxis);
                    float param = ClosestAxisParam(worldPos, dir, ray, _grabT);
                    factor = MathF.Abs(_grabT) > 1e-4f ? param / _grabT : 1f;
                }

                if (SnapKeyDown())
                    factor = MathF.Max(0.25f, MathF.Round(factor * 4f) * 0.25f);

                factor = Math.Clamp(factor, 0.01f, 100f);

                var scale = _dragStartScale;
                if (_dragAxis == 3) scale *= factor;
                else if (_dragAxis == 0) scale.X *= factor;
                else if (_dragAxis == 1) scale.Y *= factor;
                else scale.Z *= factor;

                t.Scale = Vector3.Max(scale, new Vector3(0.01f));   // nula/zaporne meritko nikdy
            }
            else
            {
                _dragAxis = -1;
            }

            return true;
        }

        HoverAxis = canStartDrag ? PickScaleHandle(transforms, entityIndex, worldPos, ray, s) : -1;

        if (HoverAxis >= 0 && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _dragAxis = HoverAxis;
            _dragStartScale = t.Scale;

            if (_dragAxis == 3)
                _grabMouseX = Raylib.GetMousePosition().X;
            else
                _grabT = ClosestAxisParam(worldPos, WorldAxis(transforms, entityIndex, _dragAxis), ray, 1f);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Osy meritka nejsou world-aligned (jedou s rotaci objektu), AABB nestaci -
    /// pouziva se vzdalenost paprsku od useku osy. Stredova koule ma prednost dle hloubky.
    /// </summary>
    private static int PickScaleHandle(Store<Transform> transforms, int entityIndex,
        Vector3 position, Ray ray, float s)
    {
        var centerHit = Raylib.GetRayCollisionSphere(ray, position, 0.14f * s);

        int best = centerHit.Hit ? 3 : -1;
        float bestRayT = centerHit.Hit ? centerHit.Distance : float.MaxValue;

        for (int a = 0; a < 3; a++)
        {
            var dir = WorldAxis(transforms, entityIndex, a);

            float axisT = Math.Clamp(ClosestAxisParam(position, dir, ray, -1f), 0.2f * s, 1.1f * s);
            var onAxis = position + dir * axisT;

            float rayT = MathF.Max(0f, Vector3.Dot(onAxis - ray.Position, ray.Direction));
            float dist = (onAxis - (ray.Position + ray.Direction * rayT)).Length();

            if (dist < 0.14f * s && rayT < bestRayT)
            {
                bestRayT = rayT;
                best = a;
            }
        }

        return best;
    }

    /// <summary>Osa objektu ve world prostoru (lokalni osa otocena world rotaci vcetne rodicu).</summary>
    private static Vector3 WorldAxis(Store<Transform> transforms, int entityIndex, int axis) =>
        Vector3.Normalize(Vector3.Transform(Axes[axis],
            TransformHierarchy.WorldRotation(transforms, entityIndex)));

    // ------------------------------------------------------------------ KRESLENI

    /// <summary>Kresli gizmo podle rezimu. Volat uvnitr BeginMode3D, az po objektech sceny.</summary>
    public void Draw(Store<Transform> transforms, int entityIndex, Camera3D camera)
    {
        Vector3 worldPos = transforms.Get(entityIndex).World.Translation;
        float s = GizmoScale(worldPos, camera);

        // Gizmo musi byt videt i "skrz" objekty. rlgl kresli v davkach -
        // pred zmenou depth stavu se rozpracovana davka MUSI vyflushovat,
        // jinak by se vypnuty depth test vztahl i na uz nakreslenou scenu.
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();

        switch (Mode)
        {
            case GizmoMode.Translate: DrawTranslate(worldPos, s); break;
            case GizmoMode.Rotate: DrawRotate(worldPos, camera); break;
            default: DrawScale(transforms, entityIndex, worldPos, s); break;
        }

        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();
    }

    private void DrawTranslate(Vector3 position, float s)
    {
        if (Dragging)
        {
            // Vodici primka pres celou scenu - je videt, po cem se jede.
            var d = Axes[_dragAxis];
            Raylib.DrawLine3D(position - d * 500f, position + d * 500f, AxisColors[_dragAxis]);
        }

        for (int a = 0; a < 3; a++)
        {
            var dir = Axes[a];
            var color = HandleColor(a);

            var tail = position + dir * (0.15f * s);   // stred nechavame volny (prekryv os)
            var neck = position + dir * (0.85f * s);
            var tip = position + dir * (1.15f * s);

            Raylib.DrawCylinderEx(tail, neck, 0.03f * s, 0.03f * s, 8, color);   // drik
            Raylib.DrawCylinderEx(neck, tip, 0.09f * s, 0f, 12, color);          // hrot
        }
    }

    private void DrawRotate(Vector3 position, Camera3D camera)
    {
        float radius = RingRadius(position, camera);

        // DrawCircle3D kresli kruznici v rovine XY, pak ji otoci kolem osy o uhel.
        // X kruznice (rovina YZ): otocit kolem Y o 90. Y kruznice (rovina XZ): kolem X o 90.
        for (int a = 0; a < 3; a++)
        {
            var color = HandleColor(a);
            var (rotAxis, angleDeg) = a switch
            {
                0 => (Vector3.UnitY, 90f),
                1 => (Vector3.UnitX, 90f),
                _ => (Vector3.UnitY, 0f),
            };

            // Tri soustredne kruznice = "tlustsi" cara, at je na trackpadu videt, co chytas.
            Raylib.DrawCircle3D(position, radius, rotAxis, angleDeg, color);
            Raylib.DrawCircle3D(position, radius * 0.985f, rotAxis, angleDeg, color);
            Raylib.DrawCircle3D(position, radius * 1.015f, rotAxis, angleDeg, color);
        }

        if (Dragging)
        {
            // Paprsky od stredu: kde jsi kruznici chytil (seda) a kde jsi ted (zluta).
            Raylib.DrawLine3D(position, position + _grabDir * radius, Color.Gray);
            Raylib.DrawLine3D(position, position + _dragCurrentDir * radius, HighlightColor);
        }
    }

    private void DrawScale(Store<Transform> transforms, int entityIndex, Vector3 position, float s)
    {
        for (int a = 0; a < 3; a++)
        {
            var dir = WorldAxis(transforms, entityIndex, a);
            var color = HandleColor(a);

            var tail = position + dir * (0.18f * s);
            var tip = position + dir * (1.0f * s);

            Raylib.DrawCylinderEx(tail, tip, 0.03f * s, 0.03f * s, 8, color);
            Raylib.DrawSphereEx(tip, 0.08f * s, 8, 8, color);   // koule = orientace je jedno
        }

        var centerColor = HoverAxis == 3 || _dragAxis == 3 ? HighlightColor : UniformColor;
        Raylib.DrawSphereEx(position, 0.11f * s, 10, 10, centerColor);
    }

    private Color HandleColor(int axis) =>
        axis == _dragAxis || (!Dragging && axis == HoverAxis) ? HighlightColor : AxisColors[axis];

    // ------------------------------------------------------------------ SPOLECNA MATIKA

    /// <summary>Velikost gizma roste se vzdalenosti kamery - na obrazovce je +- porad stejne velke.</summary>
    private static float GizmoScale(Vector3 position, Camera3D camera) =>
        MathF.Max(0.01f, Vector3.Distance(camera.Position, position) * 0.16f);

    /// <summary>Polomer rotacnich kruznic - o neco vetsi nez sipky, at se s objektem neprekryvaji.</summary>
    private static float RingRadius(Vector3 position, Camera3D camera) =>
        GizmoScale(position, camera) * 0.9f;

    /// <summary>
    /// Parametr nejblizsiho bodu na primce (origin + t * dir) vuci paprsku mysi.
    /// Standardni closest-point-of-two-lines, zjednodusene pro jednotkove smery.
    /// Kdyz je osa s paprskem skoro rovnobezna, vrati fallback (tazeni neuleti).
    /// </summary>
    private static float ClosestAxisParam(Vector3 origin, Vector3 dir, Ray ray, float fallback)
    {
        Vector3 r = origin - ray.Position;
        float b = Vector3.Dot(dir, ray.Direction);
        float d = Vector3.Dot(dir, r);
        float e = Vector3.Dot(ray.Direction, r);

        float denom = 1f - b * b;
        if (denom < 1e-4f) return fallback;

        return (b * e - d) / denom;
    }

    /// <summary>Ctrl (Windows) nebo Cmd (Mac) = prichytavani.</summary>
    private static bool SnapKeyDown() =>
        Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl) ||
        Raylib.IsKeyDown(KeyboardKey.LeftSuper) || Raylib.IsKeyDown(KeyboardKey.RightSuper);
}
