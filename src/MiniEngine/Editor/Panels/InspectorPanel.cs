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

        // --- ParticleEmitter ---
        if (_emitters.Has(e))
        {
            ImGui.Separator();
            ImGui.Text("ParticleEmitter");

            ref var emitter = ref _emitters.Get(e);
            ImGui.Checkbox("Aktivni", ref emitter.Active);

            string[] types = ["Oheň", "Kouř", "Jiskry"];
            int currentType = emitter.Type;
            if (ImGui.Combo("Typ", ref currentType, types, types.Length))
            {
                emitter.Type = currentType;
                if (currentType == 0) // Ohen
                {
                    emitter.ColorStart = new Vector3(1.0f, 0.5f, 0.1f);
                    emitter.ColorEnd = new Vector3(0.2f, 0.2f, 0.2f);
                    emitter.SizeStart = 0.2f;
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
                    emitter.SizeEnd = 0.01f;
                    emitter.Gravity = new Vector3(0f, -4f, 0f);
                    emitter.VelocityMin = new Vector3(-1.5f, 2f, -1.5f);
                    emitter.VelocityMax = new Vector3(1.5f, 5f, 1.5f);
                }
            }

            ImGui.DragFloat("Spawn Rate", ref emitter.SpawnRate, 0.5f, 1f, 200f);
            ImGui.ColorEdit3("Barva Start", ref emitter.ColorStart);
            ImGui.ColorEdit3("Barva End", ref emitter.ColorEnd);
            ImGui.DragFloat("Velikost Start", ref emitter.SizeStart, 0.01f, 0.01f, 2f);
            ImGui.DragFloat("Velikost End", ref emitter.SizeEnd, 0.01f, 0.01f, 2f);
            ImGui.DragFloat("Zivotnost", ref emitter.Lifetime, 0.05f, 0.1f, 10f);
            ImGui.DragFloat3("Gravitace", ref emitter.Gravity, 0.05f);

            if (ImGui.Button("Odebrat ParticleEmitter"))
            {
                _emitters.RemoveAt(e);
            }
        }
        else
        {
            ImGui.Separator();
            if (ImGui.Button("Přidat ParticleEmitter"))
            {
                _emitters.Add(e, ParticleEmitter.Default);
            }
        }

        // --- AudioSourceComponent ---
        if (_audioSources.Has(e))
        {
            ImGui.Separator();
            ImGui.Text("AudioSource");

            ref var source = ref _audioSources.Get(e);
            ImGui.Checkbox("Aktivni (Prehravat)", ref source.Active);

            string pathBuf = source.ClipPath ?? "";
            if (ImGui.InputText("Zvukovy soubor", ref pathBuf, 256))
            {
                source.ClipPath = pathBuf;
            }

            ImGui.SliderFloat("Hlasitost", ref source.Volume, 0f, 1f);
            ImGui.SliderFloat("Pitch", ref source.Pitch, 0.5f, 2f);
            ImGui.Checkbox("Smyckovat", ref source.Loop);
            ImGui.Checkbox("Prostorovy 3D", ref source.Spatial3D);

            if (ImGui.Button("Odebrat AudioSource"))
            {
                _audioSources.RemoveAt(e);
            }
        }
        else
        {
            ImGui.Separator();
            if (ImGui.Button("Přidat AudioSource"))
            {
                _audioSources.Add(e, AudioSourceComponent.Default);
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
