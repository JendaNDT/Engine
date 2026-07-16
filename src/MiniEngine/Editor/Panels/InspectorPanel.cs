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
    }

    public void Draw(EditorSelection selection)
    {
        ImGui.Begin("Inspector");

        int e = selection.EntityIndex;
        if (e < 0 || !_transforms.Has(e))
        {
            ImGui.TextDisabled("Nic nevybrano");
            ImGui.End();
            return;
        }

        // Sirka poli: nech 110 px vpravo na popisky, jinak se oriznou mimo okno.
        ImGui.PushItemWidth(-110f);

        // --- Nazev ---
        if (_names.Has(e))
        {
            ref var n = ref _names.Get(e);
            string buf = n.Value ?? "";
            if (ImGui.InputText("Nazev", ref buf, 64))
                n.Value = buf;
        }

        // --- Transform ---
        ImGui.Separator();
        ImGui.Text("Transform");

        ref var t = ref _transforms.Get(e);

        ImGui.DragFloat3("Pozice", ref t.Position, 0.05f);

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

        ImGui.DragFloat3("Meritko", ref t.Scale, 0.05f);

        // --- MeshRenderer ---
        if (_renderers.Has(e))
        {
            ImGui.Separator();
            ImGui.Text("MeshRenderer");

            ref var r = ref _renderers.Get(e);
            ImGui.Checkbox("Viditelny", ref r.Visible);
            ImGui.ColorEdit3("Barva", ref r.Tint);

            string texPath = r.AlbedoTexturePath ?? "";
            if (ImGui.InputText("Textura", ref texPath, 256))
            {
                r.AlbedoTexturePath = texPath;
            }
        }

        ImGui.PopItemWidth();
        ImGui.End();
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
