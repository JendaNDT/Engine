using System;
using System.Numerics;
using ImGuiNET;

namespace MiniEngine.Editor;

public sealed class ToastNotification
{
    private static int _idCounter;
    public int UniqueId { get; }
    public string Message { get; }
    public float Duration { get; }
    public float Age { get; set; }

    public ToastNotification(string message, float duration = 3.0f)
    {
        UniqueId = System.Threading.Interlocked.Increment(ref _idCounter);
        Message = message;
        Duration = duration;
        Age = 0f;
    }
}

public static class ToastSystem
{
    private static readonly System.Collections.Generic.List<ToastNotification> _toasts = new();

    public static void Show(string message, float duration = 3.0f)
    {
        lock (_toasts)
        {
            _toasts.Add(new ToastNotification(message, duration));
        }
    }

    public static void UpdateAndDraw(float dt)
    {
        lock (_toasts)
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var toast = _toasts[i];
                toast.Age += dt;
                if (toast.Age >= toast.Duration)
                {
                    _toasts.RemoveAt(i);
                }
            }

            if (_toasts.Count == 0) return;

            var io = ImGui.GetIO();
            Vector2 viewportSize = io.DisplaySize;
            float yOffset = viewportSize.Y - 20f;

            for (int i = 0; i < _toasts.Count; i++)
            {
                var toast = _toasts[i];
                float remaining = toast.Duration - toast.Age;
                float alpha = Math.Clamp(remaining / 0.5f, 0f, 1f); // Fade out v posledních 0.5s

                ImGui.SetNextWindowPos(new Vector2(viewportSize.X - 10f, yOffset), ImGuiCond.Always, new Vector2(1.0f, 1.0f));
                ImGui.SetNextWindowBgAlpha(0.85f * alpha);

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.90f, 0.90f, 0.93f, alpha));
                ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.85f, 0.70f, 0.25f, 0.7f * alpha)); // Zlatý okraj toastu
                ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);

                string title = $"##Toast{toast.UniqueId}";
                ImGui.Begin(title, ImGuiWindowFlags.NoDecoration | 
                                   ImGuiWindowFlags.AlwaysAutoResize | 
                                   ImGuiWindowFlags.NoSavedSettings | 
                                   ImGuiWindowFlags.NoFocusOnAppearing | 
                                   ImGuiWindowFlags.NoMove | 
                                   ImGuiWindowFlags.NoDocking);

                ImGui.TextUnformatted(toast.Message);
                
                // Zjistíme výšku okna PŘED zavoláním End()
                float windowHeight = ImGui.GetWindowHeight();
                ImGui.End();

                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(2);

                yOffset -= windowHeight + 8f;
            }
        }
    }
}
