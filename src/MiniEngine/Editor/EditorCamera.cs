using System.Numerics;
using Raylib_cs;

namespace MiniEngine.Editor;

/// <summary>
/// Vlastni fly kamera. NEPOUZIVAME Raylib.UpdateCamera() - ta cte globalni
/// stav mysi a netusi nic o tom, ze zrovna klikas do ImGui panelu.
/// </summary>
public sealed class EditorCamera
{
    public Camera3D Camera;

    private float _yaw = -90f;     // stupne
    private float _pitch = -20f;
    private float _speed = 8f;

    public float Sensitivity = 0.15f;

    private static readonly Vector3 DefaultPosition = new(6f, 5f, 8f);
    private const float DefaultYaw = -120f;
    private const float DefaultPitch = -20f;

    public EditorCamera()
    {
        Camera = new Camera3D
        {
            Position = DefaultPosition,
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            FovY = 60f,
            Projection = CameraProjection.Perspective
        };
        Reset();
    }

    /// <summary>Vrati kameru do vychozi pozice a natoceni (klavesa R).</summary>
    public void Reset()
    {
        Camera.Position = DefaultPosition;
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        RecalcTarget();
    }

    /// <summary>Zoom kolecka/trackpadu - posun po smeru pohledu. Funguje bez drzeni tlacitka.</summary>
    public void Zoom(float amount)
    {
        Camera.Position += ForwardVector() * amount;
        RecalcTarget();
    }

    /// <summary>Priblizi kameru na cil (klavesa F) - zachova smer pohledu, jen se presune.</summary>
    public void Focus(Vector3 target, float distance)
    {
        Camera.Position = target - ForwardVector() * distance;
        RecalcTarget();
    }

    /// <summary>Rozhlizeni - z mouse delty (drzeni prave mysi NEBO Alt+tazeni).</summary>
    public void UpdateLook(Vector2 delta)
    {
        _yaw += delta.X * Sensitivity;
        _pitch -= delta.Y * Sensitivity;
        _pitch = Math.Clamp(_pitch, -89f, 89f);
        RecalcTarget();
    }

    /// <summary>Kolecko za letu meni rychlost (jako v Unity/Unreal).</summary>
    public void AdjustSpeed(float wheel)
    {
        if (wheel != 0) _speed = Math.Clamp(_speed * (1f + wheel * 0.15f), 0.5f, 200f);
    }

    /// <summary>
    /// Pohyb klavesami - WASD i SIPKY, E/Q nahoru/dolu, Shift rychleji.
    /// Funguje kdykoliv je mys nad viewportem, bez drzeni tlacitka mysi
    /// (drzet dvouprsty klik a u toho psat je na trackpadu nepouzitelne).
    /// </summary>
    public void UpdateMovement(float dt)
    {
        Vector3 forward = ForwardVector();
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

        float v = _speed * dt;
        if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift)) v *= 3f;

        Vector3 move = Vector3.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) move += forward;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) move -= forward;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) move += right;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) move -= right;
        if (Raylib.IsKeyDown(KeyboardKey.E)) move += Vector3.UnitY;
        if (Raylib.IsKeyDown(KeyboardKey.Q)) move -= Vector3.UnitY;

        if (move != Vector3.Zero)
        {
            Camera.Position += Vector3.Normalize(move) * v;
            RecalcTarget();
        }
    }

    private Vector3 ForwardVector()
    {
        float yawRad = _yaw * MathF.PI / 180f;
        float pitchRad = _pitch * MathF.PI / 180f;
        return Vector3.Normalize(new Vector3(
            MathF.Cos(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(pitchRad),
            MathF.Sin(yawRad) * MathF.Cos(pitchRad)));
    }

    private void RecalcTarget() => Camera.Target = Camera.Position + ForwardVector();
}
