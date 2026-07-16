using MiniEngine.Engine;

namespace MiniEngine;

public static class Program
{
    // [STAThread] je POVINNY pro NativeAOT na Windows (raylib-cs issue #301).
    // Bez nej AOT build na Windows spadne pri inicializaci okna. Na macOS nevadi.
    [System.STAThread]
    public static void Main()
    {
        using var game = new Game();
        game.Run();
    }
}
