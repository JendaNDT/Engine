using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using MiniEngine.Core;
using MiniEngine.Assets;
using MiniEngine.Audio;
using MiniEngine.Engine;
using MiniEngine.Export;
using MiniEngine.Rendering;
using Transform = MiniEngine.Core.Transform;

namespace MiniEngine.Player;

// ============================================================
// MiniPlayer – přehrávač exportovaných her (Fáze 39+40).
// Po vytažení PlayLoop + SceneRenderer z Game.cs už BEZ TODO:
// stejná simulace i rendering jako v editoru, jen bez ImGui.
// ============================================================

public static class Program
{
    [System.STAThread] // povinné pro NativeAOT na Windows (raylib-cs #301)
    public static void Main()
    {
        string baseDir = AppContext.BaseDirectory;

        // 1. Nastavení hry
        var settings = new GameSettings();
        string gameJson = Path.Combine(baseDir, "game.json");
        if (File.Exists(gameJson))
            settings = JsonSerializer.Deserialize(File.ReadAllText(gameJson),
                GameJsonContext.Default.GameSettings) ?? settings;

        // 2. Okno + audio
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1280, 720, settings.Nazev);
        if (settings.Fullscreen) Raylib.ToggleBorderlessWindowed();
        Raylib.InitAudioDevice();
        Raylib.SetExitKey(KeyboardKey.Escape);

        // 3. Svět a systémy (stejné jako v editoru, bez panelů)
        var world = new World();
        var transforms = world.Store<Transform>();
        var renderers = world.Store<MeshRenderer>();
        var names = world.Store<Name>();
        var emitters = world.Store<ParticleEmitter>();
        var audioSources = world.Store<AudioSourceComponent>();
        var behaviors = world.Store<BehaviorComponent>();
        var triggers = world.Store<TriggerComponent>();
        var actions = world.Store<ActionComponent>();
        var lights = world.Store<LightComponent>();
        var prefabLinks = world.Store<PrefabLink>();
        var graphs = world.Store<BehaviorGraphComponent>();

        var transformSystem = new TransformSystem();
        var particles = new ParticleSystem();
        var audio = new AudioSystem();
        var behaviorSystem = new BehaviorSystem();

        // Assety hry žijí v assets/ vedle exe — STEJNÁ struktura jako u editoru,
        // takže AssetManager, LightingShader i PostProcessing fungují beze změn.
        // (Jen ověř, že AssetManager skládá cesty z AppContext.BaseDirectory, ne z CWD.)
        var assets = new AssetManager();

        var lighting = new LightingShader();
        var skybox = new SkyboxRenderer();
        if (!string.IsNullOrEmpty(settings.Skybox)) skybox.LoadSkybox(settings.Skybox);
        else skybox.LoadSkybox("textures/skybox.jpg");
        assets.DefaultShader = lighting.Shader;

        var postfx = new PostProcessing();
        var renderer = new SceneRenderer(world, assets, lighting, skybox, particles);
        var playLoop = new PlayLoop(world, assets, particles, audio, behaviorSystem)
        {
            PlayerName = string.IsNullOrEmpty(settings.HracEntity) ? "Player" : settings.HracEntity
        };

        // 4. Scéna — stejný serializátor jako editor
        string scenePath = Path.Combine(baseDir, "scene.json");
        SceneSerializer.Load(scenePath, world, transforms, renderers, names, emitters,
            audioSources, behaviors, triggers, actions, lights, assets, lighting, postfx,
            prefabLinks, graphs);
        transformSystem.UpdateWorldMatrices(transforms);

        // 5. Kamera + rovnou Play
        var camera = new Camera3D
        {
            Position = new Vector3(6f, 5f, 8f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            FovY = 60f,
            Projection = CameraProjection.Perspective
        };
        playLoop.Start();

        // Offscreen render target pro post-processing (stejný princip jako EditorViewport)
        int rtW = Raylib.GetScreenWidth(), rtH = Raylib.GetScreenHeight();
        var rt = Raylib.LoadRenderTexture(rtW, rtH);

        float overlayTimer = 5f;
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            playLoop.Update(dt, ref camera, inputAllowed: true);
            transformSystem.UpdateWorldMatrices(transforms);

            int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            renderer.Aspect = (float)sw / sh;
            renderer.ShadowPass(camera);

            // Realokace RT jen při skutečné změně velikosti okna (FBO je drahé)
            if (sw != rtW || sh != rtH)
            {
                Raylib.UnloadRenderTexture(rt);
                rt = Raylib.LoadRenderTexture(sw, sh);
                rtW = sw; rtH = sh;
            }

            // 1) scéna do offscreen textury
            Raylib.BeginTextureMode(rt);
            Raylib.ClearBackground(new Color(16, 19, 28, 255));
            Raylib.BeginMode3D(camera);
            renderer.DrawBase(camera); // bez gridu a bez editorových helperů
            Raylib.EndMode3D();
            Raylib.EndTextureMode();

            // 2) na obrazovku přes post-process shader (bloom/vignette/korekce ze scene.json)
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            var srcRec = new Rectangle(0, 0, rt.Texture.Width, -rt.Texture.Height);
            var dstRec = new Rectangle(0, 0, sw, sh);
            if (postfx.Enabled)
            {
                postfx.Apply();
                Raylib.BeginShaderMode(postfx.Shader);
                Raylib.DrawTexturePro(rt.Texture, srcRec, dstRec, Vector2.Zero, 0f, Color.White);
                Raylib.EndShaderMode();
            }
            else
            {
                Raylib.DrawTexturePro(rt.Texture, srcRec, dstRec, Vector2.Zero, 0f, Color.White);
            }

            // Overlay s ovládáním – zmizí po 5 s
            if (overlayTimer > 0f)
            {
                overlayTimer -= dt;
                float a = Math.Clamp(overlayTimer, 0f, 1f);
                Raylib.DrawRectangleRounded(new Rectangle(12, 12, 350, 30), 0.4f, 8,
                    new Color(15, 17, 22, (int)(180 * a)));
                Raylib.DrawText("WASD pohyb · mezernik skok · Esc konec",
                    22, 20, 16, new Color(232, 234, 241, (int)(255 * a)));
            }
            Raylib.DrawText("Vytvoreno v MiniEngine",
                Raylib.GetScreenWidth() - 190, Raylib.GetScreenHeight() - 26, 14,
                new Color(152, 160, 179, 140));

            Raylib.EndDrawing();
        }

        playLoop.Stop();
        Raylib.UnloadRenderTexture(rt);
        postfx.Dispose();
        assets.Dispose();
        renderer.Dispose();
        lighting.Dispose();
        skybox.Dispose();
        Raylib.CloseAudioDevice();
        Raylib.CloseWindow();
    }
}
