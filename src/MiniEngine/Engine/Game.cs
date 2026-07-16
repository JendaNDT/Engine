using System.Numerics;
using BepuPhysics;
using ImGuiNET;
using MiniEngine.Assets;
using MiniEngine.Core;
using MiniEngine.Editor;
using MiniEngine.Physics;
using MiniEngine.Rendering;
using MiniEngine.Audio;
using Raylib_cs;
using rlImGui_cs;
// Raylib_cs ma taky typ 'Transform' - bez aliasu je odkaz nejednoznacny (CS0104).
using Transform = MiniEngine.Core.Transform;

namespace MiniEngine.Engine;

public enum RenderMode
{
    Lit,
    Unlit,
    Wireframe
}

public sealed class Game : IDisposable
{
    private readonly World _world = new();
    private readonly AssetManager _assets = new();
    private readonly TransformSystem _transformSystem = new();
    private readonly Store<Transform> _transforms;
    private readonly Store<MeshRenderer> _renderers;
    private readonly Store<Name> _names;
    private readonly Store<RigidBodyRef> _bodies;
    private readonly Store<ParticleEmitter> _emitters;
    private readonly Store<AudioSourceComponent> _audioSources;
    private readonly Store<BehaviorComponent> _behaviors;
    private readonly Store<TriggerComponent> _triggers;
    private readonly Store<ActionComponent> _actions;
    private readonly Store<LightComponent> _lights;
    private readonly ParticleSystem _particleSystem = new();
    private readonly AudioSystem _audioSystem = new();
    private readonly BehaviorSystem _behaviorSystem = new();
    private readonly ProfilerPanel _profiler = new();
    private SkyboxRenderer _skybox = null!;
    private string _assetSearchQuery = "";
    private int _assetFilterType = 0;

    // Fyzika bezi jen kdyz je zapnuta tlacitkem. STOP = dispose celeho sveta,
    // START = novy svet postaveny z aktualnich Transformu. Zadne rucni mazani teles.
    private PhysicsWorld? _physics;

    /// <summary>Scena se uklada do pracovniho adresare - pri 'dotnet run' je to slozka projektu.</summary>
    private static string ScenePath => Path.Combine(Environment.CurrentDirectory, "scene.json");

    private EditorViewport _viewport = null!;
    private EditorCamera _camera = null!;

    private readonly EditorSelection _selection = new();
    private readonly TransformGizmo _gizmo = new();
    private bool _gizmoActive;   // gizmo si tento frame vzalo mys - picking nesmi bezet
    private bool _draggingRadius;
    private int _draggingRadiusEntity = -1;
    private HierarchyPanel _hierarchy = null!;
    private InspectorPanel _inspector = null!;
    private LightPanel _lightPanel = null!;
    private readonly System.Collections.Generic.List<string> _undoStack = new();
    private readonly System.Collections.Generic.List<string> _redoStack = new();
    private bool _showGrid = true;
    private float _gridSpacing = 1f;
    private bool _snapEnabledVal = false;
    private RenderMode _renderMode = RenderMode.Lit;

    // Delegat na DrawScene se MUSI cachovat. 'RenderScene(camera, DrawScene)'
    // by kazdy frame alokoval novy delegat (64 B/frame v hot pathu).
    private Action<Camera3D> _drawSceneCached = null!;

    private bool _altLooking;   // prave probiha Alt+tazeni (rozhlizeni)
    private BepuPhysics.BodyHandle? _playerBody;
    private int _playerEntity = -1;

    private float _assetScanTimer;
    private readonly System.Collections.Generic.List<string> _assetFiles = new();

    // Osvetleni + sdileny mesh/material pro krychle.
    // Kresleni pres DrawMesh s world matici = krychle konecne respektuji ROTACI
    // (DrawCubeV ji ignoroval) a dostanou stinovani.
    private LightingShader _lighting = null!;
    private Mesh _cubeMesh;
    private Material _cubeMaterial;

    private long _allocStart;
    private long _allocPerFrame;

    public Game()
    {
        // Store si vytahni JEDNOU, ne per frame (dictionary lookup v hot pathu = zbytecny).
        _transforms = _world.Store<Transform>();
        _renderers = _world.Store<MeshRenderer>();
        _names = _world.Store<Name>();
        _bodies = _world.Store<RigidBodyRef>();
        _emitters = _world.Store<ParticleEmitter>();
        _audioSources = _world.Store<AudioSourceComponent>();
        _behaviors = _world.Store<BehaviorComponent>();
        _triggers = _world.Store<TriggerComponent>();
        _actions = _world.Store<ActionComponent>();
        _lights = _world.Store<LightComponent>();
    }

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1600, 900, "MiniEngine");
        Raylib.InitAudioDevice();
        Raylib.SetTargetFPS(0);   // VSync ridi tempo

        rlImGui.Setup(darkTheme: true, enableDocking: true);

        // Načtení vlastního písma s podporou českých znaků (Latin Extended-A)
        unsafe
        {
            ushort[] ranges = new ushort[]
            {
                0x0020, 0x00FF, // Basic Latin + Latin-1 Supplement
                0x0100, 0x017F, // Latin Extended-A (čeština - ěščřž atd.)
                0
            };
            fixed (ushort* p = ranges)
            {
                var ioPtr = ImGui.GetIO();
                string fontPath = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "Roboto-Regular.ttf");
                if (File.Exists(fontPath))
                {
                    ioPtr.Fonts.AddFontFromFileTTF(fontPath, 16f, null, (IntPtr)p);
                    rlImGui.ReloadFonts();
                }
            }
        }

        MiniEngine.Editor.ImGuiTheme.ApplyDarkTheme();
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        // Okna jdou pretahovat JEN za titulek. Bez tohohle ImGui povazuje obraz sceny
        // za prazdne pozadi okna a Alt+tazeni (rozhlizeni) odtahne cely Viewport.
        io.ConfigWindowsMoveFromTitleBarOnly = true;

        _lighting = new LightingShader();
        _skybox = new SkyboxRenderer();
        _skybox.LoadSkybox("textures/skybox.jpg");
        _assets.DefaultShader = _lighting.Shader;   // kazdy nacteny model dostane osvetleni
        _cubeMesh = Raylib.GenMeshCube(1f, 1f, 1f);
        _cubeMaterial = Raylib.LoadMaterialDefault();
        _cubeMaterial.Shader = _lighting.Shader;

        _viewport = new EditorViewport();
        _camera = new EditorCamera();
        _hierarchy = new HierarchyPanel(_world);
        _inspector = new InspectorPanel(_world);
        _inspector.OnFocus = FocusSelection;
        _inspector.OnChanged = RecordHistoryState;
        _lightPanel = new LightPanel(_lighting, _viewport.PostProcessor);
        _drawSceneCached = DrawScene;
        _viewport.DrawToolbar = DrawViewportToolbar;   // jednou, ne per frame (delegat)
        _hierarchy.OnSave = SaveScene;
        _hierarchy.OnLoad = LoadScene;
        _hierarchy.OnDuplicate = DuplicateSelected;
        _hierarchy.OnDelete = DeleteSelected;
        _hierarchy.OnReparent = ReparentEntity;
        _hierarchy.OnChanged = RecordHistoryState;
        _lightPanel.OnChanged = RecordHistoryState;
        _hierarchy.OnCreateEmpty = CreateEmptyEntity;
        _hierarchy.OnCreateChildOf = CreateChildOf;
        _hierarchy.OnSavePrefab = SaveSelectedPrefab;

        _assets.OnAssetChanged = (path) =>
        {
            if (path.EndsWith(".vs") || path.EndsWith(".fs"))
            {
                if (_lighting.Reload())
                {
                    for (int i = 0; i < _assets.Count; i++)
                    {
                        _assets.SetShader(i, _lighting.Shader);
                    }
                    _cubeMaterial.Shader = _lighting.Shader;
                    _hierarchy.SceneStatus = "Shader uspesne hot-reloaded!";
                    ToastSystem.Show("Shader úspěšně hot-reloaded!");
                }
                else
                {
                    _hierarchy.SceneStatus = "Chyba reloadu shaderu (GLSL syntax error?)";
                    ToastSystem.Show("Chyba reloadu shaderu (GLSL syntax error?)", 4f);
                }
            }
            else
            {
                _hierarchy.SceneStatus = $"Model hot-reloaded: {Path.GetFileName(path)}";
                ToastSystem.Show($"Model hot-reloaded: {Path.GetFileName(path)}");
            }
        };

        RefreshAssetFiles();
        CreateDemoScene();
        TryLoadTestModel();   // az PO InitWindow - LoadModel nahrava na GPU
        RecordHistoryState();

        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();
            _allocStart = GC.GetAllocatedBytesForCurrentThread();

            Update(dt);

            // --- SHADOW PASS ---
            Camera3D shadowCamera = new Camera3D
            {
                Position = -_lighting.SunDirection * 35.0f,
                Target = Vector3.Zero,
                Up = new Vector3(0.0f, 1.0f, 0.0f),
                FovY = 60.0f,
                Projection = CameraProjection.Orthographic
            };

            RenderTexture2D shadowMapRT = new RenderTexture2D
            {
                Id = _lighting.ShadowFboId,
                Texture = new Texture2D { Id = 0, Width = 2048, Height = 2048 },
                Depth = new Texture2D { Id = _lighting.ShadowMapTexture.Id, Width = 2048, Height = 2048 }
            };

            Raylib.BeginTextureMode(shadowMapRT);
            Rlgl.ColorMask(false, false, false, false);
            Rlgl.ClearScreenBuffers();
            
            Raylib.BeginMode3D(shadowCamera);
            _lighting.LightMvp = Rlgl.GetMatrixModelview() * Rlgl.GetMatrixProjection();
            DrawSceneGeometries();
            Raylib.EndMode3D();
            
            Rlgl.ColorMask(true, true, true, true);
            Raylib.EndTextureMode();

            // 1) scena do offscreen textury
            _viewport.RenderScene(_camera.Camera, _drawSceneCached);

            // Merime alokace jen v Update + RenderScene (tj. v hot pathu enginu).
            // ImGui panely nize alokuji kvuli string interpolaci - to je OK, kresli se jen v editoru.
            _allocPerFrame = GC.GetAllocatedBytesForCurrentThread() - _allocStart;

            _profiler.AddFrameData(Raylib.GetFPS(), Raylib.GetFrameTime() * 1000f, _allocPerFrame, _particleSystem.GetActiveParticlesCount());

            // 2) editor
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(18, 18, 20, 255));

            rlImGui.Begin();
            ImGui.DockSpaceOverViewport();   // pozn.: overload se mezi verzemi ImGui.NET lisi

            // Vychozi rozlozeni: Hierarchy vlevo, Viewport uprostred, Inspector vpravo, Stats vlevo dole.
            // FirstUseEver = plati jen dokud okno nema ulozene rozlozeni v imgui.ini.
            var mainVp = ImGui.GetMainViewport();
            Vector2 wp = mainVp.WorkPos;
            Vector2 ws = mainVp.WorkSize;
            float leftW = MathF.Min(300f, ws.X * 0.20f);
            float rightW = MathF.Min(360f, ws.X * 0.24f);

            NextWindowRect(new Vector2(wp.X + leftW, wp.Y), new Vector2(ws.X - leftW - rightW, ws.Y * 0.70f));
            _viewport.DrawPanel(_camera.Camera);
            if (_viewport.DroppedModelPath != null)
            {
                SpawnModelAssetAt(_viewport.DroppedModelPath, _viewport.DroppedPosition);
                _viewport.DroppedModelPath = null;
            }
            if (_viewport.DroppedTexturePath != null)
            {
                ApplyTextureAtMouse(_viewport.DroppedTexturePath);
                _viewport.DroppedTexturePath = null;
            }
            if (_viewport.DroppedPrefabPath != null)
            {
                SpawnPrefabAt(_viewport.DroppedPrefabPath, _viewport.DroppedPrefabPosition);
                _viewport.DroppedPrefabPath = null;
            }
            HandleGizmo();     // PRED pickingem - klik na sipku nesmi zmenit vyber

            NextWindowRect(new Vector2(wp.X + leftW, wp.Y + ws.Y * 0.70f), new Vector2(ws.X - leftW - rightW, ws.Y * 0.30f));
            DrawAssetBrowserPanel();
            HandlePicking();   // hned po DrawPanel - Hovered a Origin jsou cerstvé

            NextWindowRect(new Vector2(wp.X, wp.Y), new Vector2(leftW, ws.Y * 0.58f));
            _hierarchy.Draw(_selection);

            NextWindowRect(new Vector2(wp.X + ws.X - rightW, wp.Y), new Vector2(rightW, ws.Y * 0.62f));
            _inspector.Draw(_selection);

            NextWindowRect(new Vector2(wp.X + ws.X - rightW, wp.Y + ws.Y * 0.62f), new Vector2(rightW, ws.Y * 0.38f));
            _lightPanel.Draw();

            NextWindowRect(new Vector2(wp.X, wp.Y + ws.Y * 0.58f), new Vector2(leftW * 0.4f, ws.Y * 0.42f));
            DrawStatsPanel();

            NextWindowRect(new Vector2(wp.X + leftW * 0.4f, wp.Y + ws.Y * 0.58f), new Vector2(leftW * 0.6f, ws.Y * 0.42f));
            _profiler.Draw();

            MiniEngine.Editor.ToastSystem.UpdateAndDraw(dt);

            rlImGui.End();

            Raylib.EndDrawing();
        }

        StopPhysics();
        _assets.Dispose();    // GPU zdroje uvolnit PRED zavrenim okna (vraci materialum default shader)
        Raylib.UnloadMesh(_cubeMesh);
        _lighting.Dispose();
        _skybox.Dispose();
        _viewport.Dispose();
        rlImGui.Shutdown();
        Raylib.CloseAudioDevice();
        Raylib.CloseWindow();
    }

    private void Update(float dt)
    {
        _behaviorSystem.Update(dt, _transforms, _behaviors, _bodies, _physics);
        _particleSystem.Update(dt, _transforms, _emitters);
        _audioSystem.Update(_camera.Camera, _transforms, _audioSources, _assets);
        UpdateTriggers();

        _assetScanTimer += dt;
        if (_assetScanTimer > 2f)
        {
            _assetScanTimer = 0f;
            RefreshAssetFiles();
        }

        bool altDown = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);

        // Alt+tazeni se po startu "latchne": rozhlizis se, dokud nepustis tlacitko,
        // i kdyz kurzor mezitim vyjede z viewportu. Start jen nad viewportem.
        if (_altLooking)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
                _camera.UpdateLook(Raylib.GetMouseDelta());
            else
                _altLooking = false;
        }
        else if (!_viewport.Captured && _viewport.Hovered && altDown &&
                 Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _altLooking = true;
        }

        // Rozhlizeni drzenim prave mysi (klasika pro mys s tlacitky).
        if (_viewport.Captured)
        {
            _camera.UpdateLook(Raylib.GetMouseDelta());
            _camera.AdjustSpeed(Raylib.GetMouseWheelMove());
        }
        else if (!_altLooking && _viewport.Hovered)
        {
            // Zoom koleckem/trackpadem - bez drzeni cehokoliv.
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0)
                _camera.Zoom(wheel * 1.2f);
        }

        // Pohyb klavesami funguje vzdy, kdyz je mys nad viewportem (nebo za letu).
        // Gate pres GameKeyboardAllowed: pri psani do textovych poli se kamera nehybe.
        if (EditorViewport.GameKeyboardAllowed)
        {
            // Cmd (Mac) / Ctrl (Windows) prepina klavesy do "zkratkoveho" rezimu -
            // Cmd+D nesmi zaroven cukat kamerou doprava (D je i pohyb).
            bool cmdCtrl = Raylib.IsKeyDown(KeyboardKey.LeftSuper) || Raylib.IsKeyDown(KeyboardKey.RightSuper) ||
                           Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);

            if ((_viewport.Hovered || _viewport.Captured) && !cmdCtrl)
                _camera.UpdateMovement(dt);

            if (Raylib.IsKeyPressed(KeyboardKey.F)) FocusSelection();
            if (cmdCtrl && Raylib.IsKeyPressed(KeyboardKey.D)) DuplicateSelected();
            if (cmdCtrl && Raylib.IsKeyPressed(KeyboardKey.Z)) Undo();
            if (cmdCtrl && Raylib.IsKeyPressed(KeyboardKey.Y)) Redo();
            if (Raylib.IsKeyPressed(KeyboardKey.Delete) || Raylib.IsKeyPressed(KeyboardKey.Backspace)) DeleteSelected();

            // Rezim gizma jako v Unity/Unreal (W/E/R, pokud se nerozhlizime kamerou)
            if (!_viewport.Captured && !_altLooking)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.W)) _gizmo.SetMode(GizmoMode.Translate);
                if (Raylib.IsKeyPressed(KeyboardKey.E)) _gizmo.SetMode(GizmoMode.Rotate);
                if (Raylib.IsKeyPressed(KeyboardKey.R)) _gizmo.SetMode(GizmoMode.Scale);
                if (Raylib.IsKeyPressed(KeyboardKey.L)) _gizmo.LocalSpace = !_gizmo.LocalSpace;
            }
            else
            {
                if (Raylib.IsKeyPressed(KeyboardKey.R)) _camera.Reset();
            }
        }

        // Fyzika: fixed timestep + interpolace, tela jsou zdroj pravdy pro Transform.
        // Fyzika mysli ve WORLD prostoru - u deti v hierarchii se poza prepocita
        // na lokalni pres rodicovu World matici (z minuleho framu, to je ok).
        if (_physics is not null)
        {
            if (_playerBody.HasValue && _physics.Simulation.Bodies.BodyExists(_playerBody.Value))
            {
                var bodyRef = _physics.Simulation.Bodies[_playerBody.Value];
                Vector3 currentVel = bodyRef.Velocity.Linear;

                Vector3 camDir = new Vector3(_camera.Camera.Target.X - _camera.Camera.Position.X, 0f, _camera.Camera.Target.Z - _camera.Camera.Position.Z);
                Vector3 camForward = camDir.LengthSquared() > 0.001f ? Vector3.Normalize(camDir) : new Vector3(0f, 0f, -1f);
                Vector3 camRight = Vector3.Normalize(Vector3.Cross(camForward, _camera.Camera.Up));

                Vector3 moveDir = Vector3.Zero;
                if (Raylib.IsKeyDown(KeyboardKey.W)) moveDir += camForward;
                if (Raylib.IsKeyDown(KeyboardKey.S)) moveDir -= camForward;
                if (Raylib.IsKeyDown(KeyboardKey.D)) moveDir += camRight;
                if (Raylib.IsKeyDown(KeyboardKey.A)) moveDir -= camRight;

                if (moveDir != Vector3.Zero)
                    moveDir = Vector3.Normalize(moveDir);

                float speed = 8f;
                Vector3 targetVel = moveDir * speed;

                bodyRef.Velocity.Linear = new Vector3(targetVel.X, currentVel.Y, targetVel.Z);

                if (Raylib.IsKeyPressed(KeyboardKey.Space) && MathF.Abs(currentVel.Y) < 0.1f)
                {
                    bodyRef.Velocity.Linear = new Vector3(bodyRef.Velocity.Linear.X, 6f, bodyRef.Velocity.Linear.Z);
                }

                Vector3 playerPos = bodyRef.Pose.Position;
                Vector3 camOffset = _camera.Camera.Position - _camera.Camera.Target;
                _camera.Camera.Target = playerPos;
                _camera.Camera.Position = playerPos + camOffset;
            }

            float alpha = _physics.Step(dt);

            var bodies = _bodies.Span;
            var bodyEntities = _bodies.Entities;
            for (int i = 0; i < bodies.Length; i++)
            {
                ref var b = ref bodies[i];
                var (pos, rot) = _physics.GetInterpolated(new BodyHandle(b.BodyHandle), alpha);
                Vector3 visualPos = pos - Vector3.Transform(b.CenterOffset, rot);
                TransformHierarchy.SetWorldPosition(_transforms, bodyEntities[i], visualPos);
                TransformHierarchy.SetWorldRotation(_transforms, bodyEntities[i], rot);
            }

            _inspector.InvalidateRotationCache();
        }

        _assets.UpdateHotReload();
        _transformSystem.UpdateWorldMatrices(_transforms);
    }

    private void StartPhysics()
    {
        _physics = new PhysicsWorld();

        // Neviditelna podlaha v urovni gridu, at je kam padat.
        _physics.AddStaticBox(new Vector3(0f, -0.25f, 0f), new Vector3(400f, 0.5f, 400f));

        int playerIdx = -1;
        var names = _names.Span;
        var nameEntities = _names.Entities;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i].Value, "Player", StringComparison.OrdinalIgnoreCase))
            {
                playerIdx = nameEntities[i];
                break;
            }
        }

        // Kazda viditelna entita s rendererem dostane dynamicke telo.
        var renderers = _renderers.Span;
        var entities = _renderers.Entities;
        for (int i = 0; i < renderers.Length; i++)
        {
            ref var r = ref renderers[i];
            if (!r.Visible) continue;

            int idx = entities[i];
            ref var t = ref _transforms.Get(idx);

            var worldScale = TransformHierarchy.WorldScale(t.World);
            float mass = MathF.Max(0.1f, worldScale.X * worldScale.Y * worldScale.Z);

            if (idx == playerIdx)
            {
                float radius = 0.4f;
                float height = 1.0f;
                var handle = _physics.AddCharacter(
                    t.World.Translation,
                    radius, height, mass);
                _bodies.Add(idx, new RigidBodyRef { BodyHandle = handle.Value, IsKinematic = false, CenterOffset = Vector3.Zero });
                _playerBody = handle;
                _playerEntity = idx;
            }
            else if (r.ModelHandle >= 0)
            {
                ref Model m = ref _assets.Get(r.ModelHandle);
                var vertexList = new System.Collections.Generic.List<Vector3>();

                unsafe
                {
                    for (int mi = 0; mi < m.MeshCount; mi++)
                    {
                        ref var mesh = ref m.Meshes[mi];
                        if (mesh.Vertices != null && mesh.VertexCount > 0)
                        {
                            for (int vi = 0; vi < mesh.VertexCount; vi++)
                            {
                                float x = mesh.Vertices[vi * 3];
                                float y = mesh.Vertices[vi * 3 + 1];
                                float z = mesh.Vertices[vi * 3 + 2];
                                vertexList.Add(new Vector3(x * worldScale.X, y * worldScale.Y, z * worldScale.Z));
                            }
                        }
                    }
                }

                if (vertexList.Count > 0)
                {
                    var handle = _physics.AddDynamicConvexHull(
                        t.World.Translation,
                        TransformHierarchy.WorldRotation(_transforms, idx),
                        vertexList.ToArray(), mass, out var center);
                    _bodies.Add(idx, new RigidBodyRef { BodyHandle = handle.Value, IsKinematic = false, CenterOffset = center });
                }
                else
                {
                    var handle = _physics.AddDynamicBox(
                        t.World.Translation,
                        TransformHierarchy.WorldRotation(_transforms, idx),
                        worldScale, mass);
                    _bodies.Add(idx, new RigidBodyRef { BodyHandle = handle.Value, IsKinematic = false, CenterOffset = Vector3.Zero });
                }
            }
            else
            {
                var handle = _physics.AddDynamicBox(
                    t.World.Translation,
                    TransformHierarchy.WorldRotation(_transforms, idx),
                    worldScale, mass);
                _bodies.Add(idx, new RigidBodyRef { BodyHandle = handle.Value, IsKinematic = false, CenterOffset = Vector3.Zero });
            }
        }
    }

    private void StopPhysics()
    {
        _physics?.Dispose();
        _physics = null;
        _bodies.Clear();   // Transformy zustanou tam, kde tela dopadla
        _playerBody = null;
        _playerEntity = -1;
        _audioSystem.StopAll(_audioSources, _assets);
        _behaviorSystem.RestoreInitialStates(_transforms, _behaviors);
    }

    private void SaveScene()
    {
        try
        {
            SceneSerializer.Save(ScenePath, _transforms, _renderers, _names, _emitters, _audioSources, _behaviors, _triggers, _actions, _lights, _assets, _lighting, _viewport.PostProcessor);
            _hierarchy.SceneStatus = "Ulozeno: scene.json";
            ToastSystem.Show("Scéna uložena: scene.json");
        }
        catch (Exception ex)
        {
            _hierarchy.SceneStatus = $"Chyba ukladani: {ex.Message}";
            ToastSystem.Show($"Chyba ukládání: {ex.Message}", 4f);
        }
    }

    private void LoadScene()
    {
        if (!File.Exists(ScenePath))
        {
            _hierarchy.SceneStatus = "scene.json neexistuje - nejdriv uloz";
            ToastSystem.Show("Soubor scene.json neexistuje!", 4f);
            return;
        }

        StopPhysics();   // nacitani za behu fyziky = tela by ukazovala na smazane entity

        try
        {
            SceneSerializer.Load(ScenePath, _world, _transforms, _renderers, _names, _emitters, _audioSources, _behaviors, _triggers, _actions, _lights, _assets, _lighting, _viewport.PostProcessor);
            _lightPanel.LoadFrom(_lighting.SunDirection);
            _selection.Clear();
            _hierarchy.SceneStatus = "Nacteno: scene.json";
            ToastSystem.Show("Scéna načtena: scene.json");
        }
        catch (Exception ex)
        {
            _hierarchy.SceneStatus = $"Chyba nacitani: {ex.Message}";
            ToastSystem.Show($"Chyba načítání: {ex.Message}", 4f);
        }
    }

    /// <summary>Prileti kamerou k vybranemu objektu (klavesa F, jako v Unity).</summary>
    private void FocusSelection()
    {
        if (!_selection.HasSelection || !_transforms.Has(_selection.EntityIndex)) return;

        ref var t = ref _transforms.Get(_selection.EntityIndex);
        var worldScale = TransformHierarchy.WorldScale(t.World);
        float extent = MathF.Max(worldScale.X, MathF.Max(worldScale.Y, worldScale.Z));
        _camera.Focus(t.World.Translation, extent * 3f + 2f);
    }

    /// <summary>Prevesi entitu pod noveho rodice (-1 = koren), world poza zustava.</summary>
    private void ReparentEntity(int child, int newParent)
    {
        if (!TransformHierarchy.Reparent(_transforms, child, newParent))
            _hierarchy.SceneStatus = "Prevesit nejde (cyklus nebo neplatny cil)";
        else
            RecordHistoryState();
    }

    private void DrawViewportToolbar()
    {
        // Levá část: Transformace a přichytávání
        GizmoModeButton("Posun (W)", GizmoMode.Translate);
        ImGui.SameLine();
        GizmoModeButton("Rotace (E)", GizmoMode.Rotate);
        ImGui.SameLine();
        GizmoModeButton("Měřítko (R)", GizmoMode.Scale);

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        string spaceLabel = _gizmo.LocalSpace ? "Lokální" : "Globální";
        if (ImGui.Button(spaceLabel))
        {
            _gizmo.LocalSpace = !_gizmo.LocalSpace;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        ImGui.Checkbox("Přichytávat", ref _snapEnabledVal);
        _gizmo.SnapEnabled = _snapEnabledVal;

        ImGui.SameLine();
        ImGui.PushItemWidth(65f);
        string[] snapSteps = ["0.1", "0.25", "0.5", "1.0", "2.0"];
        float[] snapVals = [0.1f, 0.25f, 0.5f, 1.0f, 2.0f];
        int currentSnapIdx = Array.IndexOf(snapVals, _gizmo.SnapMoveStep);
        if (currentSnapIdx < 0) currentSnapIdx = 2;
        if (ImGui.Combo("##SnapCombo", ref currentSnapIdx, snapSteps, snapSteps.Length))
        {
            _gizmo.SnapMoveStep = snapVals[currentSnapIdx];
            _gizmo.SnapRotateStepDeg = currentSnapIdx switch
            {
                0 => 5f,
                1 => 10f,
                2 => 15f,
                3 => 30f,
                _ => 45f
            };
            _gizmo.SnapScaleStep = currentSnapIdx switch
            {
                0 => 0.05f,
                1 => 0.1f,
                2 => 0.25f,
                3 => 0.5f,
                _ => 1.0f
            };
        }
        ImGui.PopItemWidth();

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        ImGui.Checkbox("Mřížka", ref _showGrid);

        ImGui.SameLine();
        ImGui.PushItemWidth(60f);
        string[] gridSteps = ["0.5", "1.0", "2.0", "5.0"];
        float[] gridVals = [0.5f, 1.0f, 2.0f, 5.0f];
        int currentGridIdx = Array.IndexOf(gridVals, _gridSpacing);
        if (currentGridIdx < 0) currentGridIdx = 1;
        if (ImGui.Combo("##GridSpacing", ref currentGridIdx, gridSteps, gridSteps.Length))
        {
            _gridSpacing = gridVals[currentGridIdx];
        }
        ImGui.PopItemWidth();

        // Pravá část: Režim vykreslování, Fyzika, Kamera
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        ImGui.PushItemWidth(90f);
        string[] renderModes = ["Lit (Stínovaný)", "Unlit (Bez světel)", "Drátěný"];
        int currentMode = (int)_renderMode;
        if (ImGui.Combo("##RenderMode", ref currentMode, renderModes, renderModes.Length))
        {
            _renderMode = (RenderMode)currentMode;
        }
        ImGui.PopItemWidth();

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        if (_physics is null)
        {
            if (ImGui.Button("Spustit fyziku")) StartPhysics();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.75f, 0.24f, 0.24f, 1f));
            if (ImGui.Button("Zastavit fyziku")) StopPhysics();
            ImGui.PopStyleColor();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset kamery"))
            _camera.Reset();

        ImGui.SameLine();
        if (ImGui.Button("Fokus (F)"))
            FocusSelection();
    }

    /// <summary>Tlacitko rezimu gizma - aktivni rezim je zvyraznene.</summary>
    private void GizmoModeButton(string label, GizmoMode mode)
    {
        bool active = _gizmo.Mode == mode;
        if (active) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.24f, 0.42f, 0.75f, 1f));
        if (ImGui.Button(label)) _gizmo.SetMode(mode);
        if (active) ImGui.PopStyleColor();
    }

    private void DrawSceneGeometries()
    {
        var transforms = _transforms.Span;
        var renderers = _renderers.Span;
        var entities = _renderers.Entities;

        Shader currentShader = (_renderMode == RenderMode.Lit) ? _lighting.Shader : default;
        if (_renderMode == RenderMode.Wireframe)
        {
            Rlgl.EnableWireMode();
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            ref var r = ref renderers[i];
            if (!r.Visible) continue;

            ref var t = ref _transforms.Get(entities[i]);

            if (r.ModelHandle >= 0)
            {
                var world = Matrix4x4.Transpose(t.World);
                ref Model m = ref _assets.Get(r.ModelHandle);

                Texture2D texOverride = default;
                if (!string.IsNullOrEmpty(r.AlbedoTexturePath))
                {
                    texOverride = _assets.GetTexture(r.AlbedoTexturePath);
                }

                unsafe
                {
                    for (int mi = 0; mi < m.MeshCount; mi++)
                    {
                        ref var mat = ref m.Materials[m.MeshMaterial[mi]];
                        Shader origShader = mat.Shader;
                        mat.Shader = currentShader;

                        Color origColor = mat.Maps[(int)MaterialMapIndex.Albedo].Color;
                        Texture2D origTex = mat.Maps[(int)MaterialMapIndex.Albedo].Texture;
                        Texture2D origNormal = mat.Maps[(int)MaterialMapIndex.Normal].Texture;
                        Texture2D origRoughness = mat.Maps[(int)MaterialMapIndex.Roughness].Texture;

                        byte rVal = (byte)Math.Clamp(origColor.R * r.Tint.X, 0f, 255f);
                        byte gVal = (byte)Math.Clamp(origColor.G * r.Tint.Y, 0f, 255f);
                        byte bVal = (byte)Math.Clamp(origColor.B * r.Tint.Z, 0f, 255f);
                        mat.Maps[(int)MaterialMapIndex.Albedo].Color = new Color(rVal, gVal, bVal, origColor.A);

                        if (texOverride.Id > 0)
                        {
                            mat.Maps[(int)MaterialMapIndex.Albedo].Texture = texOverride;
                        }

                        // Normal map
                        bool hasNormal = !string.IsNullOrEmpty(r.NormalMapPath);
                        if (hasNormal)
                        {
                            var normTex = _assets.GetTexture(r.NormalMapPath);
                            if (normTex.Id > 0)
                            {
                                mat.Maps[(int)MaterialMapIndex.Normal].Texture = normTex;
                            }
                            else hasNormal = false;
                        }
                        Raylib.SetShaderValue(_lighting.Shader, _lighting.LocHasNormalMap, hasNormal ? 1 : 0, ShaderUniformDataType.Int);

                        // Metallic/Roughness map
                        bool hasMR = !string.IsNullOrEmpty(r.MetallicRoughnessMapPath);
                        if (hasMR)
                        {
                            var mrTex = _assets.GetTexture(r.MetallicRoughnessMapPath);
                            if (mrTex.Id > 0)
                            {
                                mat.Maps[(int)MaterialMapIndex.Roughness].Texture = mrTex;
                            }
                            else hasMR = false;
                        }
                        Raylib.SetShaderValue(_lighting.Shader, _lighting.LocHasMetallicRoughnessMap, hasMR ? 1 : 0, ShaderUniformDataType.Int);

                        // Factors
                        Raylib.SetShaderValue(_lighting.Shader, _lighting.LocMetallicFactor, r.MetallicFactor, ShaderUniformDataType.Float);
                        Raylib.SetShaderValue(_lighting.Shader, _lighting.LocRoughnessFactor, r.RoughnessFactor, ShaderUniformDataType.Float);

                        Raylib.DrawMesh(m.Meshes[mi], mat, world);

                        mat.Shader = origShader;
                        mat.Maps[(int)MaterialMapIndex.Albedo].Color = origColor;
                        mat.Maps[(int)MaterialMapIndex.Albedo].Texture = origTex;
                        mat.Maps[(int)MaterialMapIndex.Normal].Texture = origNormal;
                        mat.Maps[(int)MaterialMapIndex.Roughness].Texture = origRoughness;
                    }
                }
            }
            else
            {
                var color = new Color((byte)(r.Tint.X * 255), (byte)(r.Tint.Y * 255), (byte)(r.Tint.Z * 255), (byte)255);

                Texture2D texOverride = default;
                if (!string.IsNullOrEmpty(r.AlbedoTexturePath))
                {
                    texOverride = _assets.GetTexture(r.AlbedoTexturePath);
                }

                unsafe
                {
                    _cubeMaterial.Maps[(int)MaterialMapIndex.Albedo].Color = color;
                    Texture2D origTex = _cubeMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture;
                    Texture2D origNormal = _cubeMaterial.Maps[(int)MaterialMapIndex.Normal].Texture;
                    Texture2D origRoughness = _cubeMaterial.Maps[(int)MaterialMapIndex.Roughness].Texture;

                    if (texOverride.Id > 0)
                    {
                        _cubeMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = texOverride;
                    }

                    // Normal map
                    bool hasNormal = !string.IsNullOrEmpty(r.NormalMapPath);
                    if (hasNormal)
                    {
                        var normTex = _assets.GetTexture(r.NormalMapPath);
                        if (normTex.Id > 0)
                            _cubeMaterial.Maps[(int)MaterialMapIndex.Normal].Texture = normTex;
                        else hasNormal = false;
                    }
                    Raylib.SetShaderValue(_lighting.Shader, _lighting.LocHasNormalMap, hasNormal ? 1 : 0, ShaderUniformDataType.Int);

                    // Metallic/Roughness map
                    bool hasMR = !string.IsNullOrEmpty(r.MetallicRoughnessMapPath);
                    if (hasMR)
                    {
                        var mrTex = _assets.GetTexture(r.MetallicRoughnessMapPath);
                        if (mrTex.Id > 0)
                            _cubeMaterial.Maps[(int)MaterialMapIndex.Roughness].Texture = mrTex;
                        else hasMR = false;
                    }
                    Raylib.SetShaderValue(_lighting.Shader, _lighting.LocHasMetallicRoughnessMap, hasMR ? 1 : 0, ShaderUniformDataType.Int);

                    Raylib.SetShaderValue(_lighting.Shader, _lighting.LocMetallicFactor, r.MetallicFactor, ShaderUniformDataType.Float);
                    Raylib.SetShaderValue(_lighting.Shader, _lighting.LocRoughnessFactor, r.RoughnessFactor, ShaderUniformDataType.Float);

                    _cubeMaterial.Shader = currentShader;
                    Raylib.DrawMesh(_cubeMesh, _cubeMaterial, Matrix4x4.Transpose(t.World));

                    _cubeMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = origTex;
                    _cubeMaterial.Maps[(int)MaterialMapIndex.Normal].Texture = origNormal;
                    _cubeMaterial.Maps[(int)MaterialMapIndex.Roughness].Texture = origRoughness;
                }
            }
        }

        if (_renderMode == RenderMode.Wireframe)
        {
            Rlgl.DisableWireMode();
        }
    }

    private void DrawScene(Camera3D camera)
    {
        _skybox.Draw(camera);

        _lighting.Apply(camera);   // viewPos + parametry slunce do shaderu

        if (_showGrid && _gridSpacing > 0f)
        {
            Raylib.DrawGrid((int)(40f / _gridSpacing), _gridSpacing);
        }

        DrawSceneGeometries();

        _particleSystem.Draw(camera, _assets);

        // Vizuální 3D helpery v editoru (pouze když fyzika neběží)
        if (_physics is null)
        {
            var transformsSpan = _transforms.Span;
            var trEntities = _transforms.Entities;
            for (int i = 0; i < transformsSpan.Length; i++)
            {
                int idx = trEntities[i];
                var center = transformsSpan[i].World.Translation;
                
                // Pokud nemá viditelný renderer, nakreslíme drátěný pomocný tvar
                bool hasRenderer = _renderers.Has(idx) && _renderers.Get(idx).Visible;
                
                if (!hasRenderer)
                {
                    bool hasAudio = _audioSources.Has(idx);
                    bool hasEmitter = _emitters.Has(idx);
                    bool hasLight = _lights.Has(idx);
                    bool isEmpty = !_renderers.Has(idx); // prázdný objekt (složka)
                    
                    if (hasAudio)
                    {
                        // Reprezentuje reproduktor: zelená kostička s kuželem
                        Raylib.DrawCubeWires(center, 0.25f, 0.25f, 0.25f, Color.Lime);
                        Raylib.DrawCylinderWires(center, 0f, 0.2f, 0.3f, 8, Color.Lime);
                    }
                    else if (hasEmitter)
                    {
                        // Reprezentuje emitor: oranžový kužel/válec ukazující směr gravitace/emise
                        Raylib.DrawCylinderWires(center, 0.1f, 0.2f, 0.4f, 8, Color.Orange);
                        Raylib.DrawCubeWires(center, 0.15f, 0.15f, 0.15f, Color.Orange);
                    }
                    else if (hasLight)
                    {
                        // Reprezentuje světlo: žlutá kulička s paprsky
                        Raylib.DrawSphereWires(center, 0.2f, 8, 8, Color.Yellow);
                        for (int k = 0; k < 8; k++)
                        {
                            float angle = k * MathF.PI / 4f;
                            Vector3 dir = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                            Raylib.DrawLine3D(center + dir * 0.2f, center + dir * 0.4f, Color.Yellow);
                        }
                    }
                    else if (isEmpty)
                    {
                        // Reprezentuje prázdný objekt: šedá kulička a 3D kříž
                        Raylib.DrawSphereWires(center, 0.15f, 8, 8, Color.Gray);
                        Raylib.DrawLine3D(center - Vector3.UnitX * 0.25f, center + Vector3.UnitX * 0.25f, Color.Gray);
                        Raylib.DrawLine3D(center - Vector3.UnitY * 0.25f, center + Vector3.UnitY * 0.25f, Color.Gray);
                        Raylib.DrawLine3D(center - Vector3.UnitZ * 0.25f, center + Vector3.UnitZ * 0.25f, Color.Gray);
                    }
                }

                if (_triggers.Has(idx))
                {
                    var trigger = _triggers.Get(idx);
                    if (trigger.Active)
                    {
                        Raylib.DrawCubeWiresV(center, trigger.Size, Color.Green);
                    }
                }

                if (_lights.Has(idx))
                {
                    var light = _lights.Get(idx);
                    if (light.Active)
                    {
                        // Kreslíme slabou žlutou kouli představující dosah světla
                        Raylib.DrawSphereWires(center, light.Radius, 8, 8, new Color(255, 235, 150, 40));

                        // Pokud je entita vybraná, vykreslíme i interaktivní gizmo táhlo pro změnu poloměru
                        if (idx == _selection.EntityIndex)
                        {
                            Vector3 handlePos = center + Vector3.UnitX * light.Radius;
                            Ray mouseRay = _viewport.GetPickRay(camera);
                            bool handleHovered = Raylib.GetRayCollisionSphere(mouseRay, handlePos, 0.25f).Hit;
                            Color handleCol = _draggingRadius ? Color.Red : (handleHovered ? Color.Yellow : Color.Gold);

                            // Kreslíme spojovací čáru od středu k táhlu
                            Raylib.DrawLine3D(center, handlePos, Color.Gold);

                            // Kreslíme samotné táhlo (krychlička)
                            Raylib.DrawCube(handlePos, 0.15f, 0.15f, 0.15f, handleCol);
                            Raylib.DrawCubeWires(handlePos, 0.15f, 0.15f, 0.15f, Color.Black);
                        }
                    }
                }
                
                // Zvýraznění drátěného obrysu pro vybrané nevizuální entity
                if (idx == _selection.EntityIndex && !hasRenderer)
                {
                    Raylib.DrawSphereWires(center, 0.35f, 10, 10, Color.Yellow);
                }
            }
        }

        // Kresleni obrysu (wires) pro vybrane objekty
        var renderers = _renderers.Span;
        var entities = _renderers.Entities;
        for (int i = 0; i < renderers.Length; i++)
        {
            ref var r = ref renderers[i];
            if (!r.Visible) continue;

            int idx = entities[i];
            if (idx == _selection.EntityIndex)
            {
                ref var t = ref _transforms.Get(idx);
                var worldScale = TransformHierarchy.WorldScale(t.World);
                if (r.ModelHandle >= 0)
                    Raylib.DrawCubeWiresV(t.World.Translation, worldScale * 2.4f, Color.Yellow);
                else
                    Raylib.DrawCubeWiresV(t.World.Translation, worldScale * 1.08f, Color.Yellow);
            }
        }

        // Gizmo az PO objektech (kresli se pres ne, s vypnutym depth testem).
        // Za behu fyziky se nekresli - stejna podminka jako v HandleGizmo.
        if (_physics is null && _selection.HasSelection && _transforms.Has(_selection.EntityIndex))
            _gizmo.Draw(_transforms, _selection.EntityIndex, camera);
    }

    /// <summary>
    /// Interakce s translate gizmem. Vola se hned po DrawPanel (cerstve Hovered/Origin)
    /// a PRED HandlePicking - kdyz klik chytne sipku, picking uz nesmi menit vyber.
    /// </summary>
    private void HandleGizmo()
    {
        _gizmoActive = false;

        // Za behu fyziky gizmo nejede - Transformy ridi fyzikalni tela,
        // rucni posun by fyzika okamzite prepsala.
        if (_physics is not null || !_selection.HasSelection || !_transforms.Has(_selection.EntityIndex))
        {
            _gizmo.CancelDrag();
            _draggingRadius = false;
            return;
        }

        bool altDown = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
        bool canStartDrag = _viewport.Hovered && !_viewport.Captured && !_altLooking && !altDown;

        // Fáze 12: Interakce s light radius handlem
        int selIdx = _selection.EntityIndex;
        if (_lights.Has(selIdx))
        {
            ref var light = ref _lights.Get(selIdx);
            Vector3 center = _transforms.Get(selIdx).World.Translation;
            Vector3 handlePos = center + Vector3.UnitX * light.Radius;
            Ray mouseRay = _viewport.GetPickRay(_camera.Camera);
            bool handleHovered = Raylib.GetRayCollisionSphere(mouseRay, handlePos, 0.25f).Hit;

            if (_draggingRadius)
            {
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    _gizmoActive = true;
                    // Výpočet nového poloměru
                    float t = Vector3.Dot(center - mouseRay.Position, mouseRay.Direction);
                    Vector3 closest = mouseRay.Position + mouseRay.Direction * t;
                    light.Radius = MathF.Max(0.1f, Vector3.Distance(center, closest));
                }
                else
                {
                    _draggingRadius = false;
                    RecordHistoryState();
                }
            }
            else if (handleHovered && canStartDrag && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _draggingRadius = true;
                _draggingRadiusEntity = selIdx;
                _gizmoActive = true;
            }

            if (_draggingRadius || handleHovered)
            {
                _gizmoActive = true;
            }
        }

        bool wasDragging = _gizmo.Dragging;

        _gizmoActive |= _gizmo.UpdateInteraction(_transforms, _selection.EntityIndex,
            _viewport.GetPickRay(_camera.Camera), _camera.Camera, canStartDrag);

        if (wasDragging && !_gizmo.Dragging)
        {
            RecordHistoryState();
        }

        // Rotace gizmem meni kvaternion mimo Inspector - jeho euler cache by jinak
        // ukazovala stare hodnoty, dokud neprepnes vyber (znamy bug ze statusu).
        if (_gizmo.Dragging && _gizmo.Mode == GizmoMode.Rotate)
            _inspector.InvalidateRotationCache();
    }

    /// <summary>
    /// Duplikuje vybranou entitu (Transform + MeshRenderer + Name s priponou „kopie").
    /// Kopie se posune o kus vedle a rovnou se vybere, at se da hned tahnout gizmem.
    /// Cmd+D / Ctrl+D nebo tlacitko v Hierarchy.
    /// </summary>
    private void DuplicateSelected()
    {
        if (_physics is not null)
        {
            ToastSystem.Show("Během spuštěné fyziky nelze duplikovat objekty!", 3f);
            return;
        }
        if (!_selection.HasSelection || !_transforms.Has(_selection.EntityIndex)) return;

        int rootSrc = _selection.EntityIndex;

        // 1. Najdeme vsechny potomky vybrane entity
        var subtree = new System.Collections.Generic.List<int> { rootSrc };
        var entities = _transforms.Entities;

        for (int i = 0; i < entities.Length; i++)
        {
            int ent = entities[i];
            if (ent != rootSrc && TransformHierarchy.IsDescendantOf(_transforms, ent, rootSrc))
            {
                subtree.Add(ent);
            }
        }

        // 2. Vytvorime kopie vsech entit a ulozime mapovani: stary index -> nova Entity
        var oldToNew = new System.Collections.Generic.Dictionary<int, Entity>();
        foreach (int src in subtree)
        {
            oldToNew[src] = _world.Create();
        }

        // 3. Zkopirujeme komponenty a upravime parenty
        foreach (int src in subtree)
        {
            Entity dest = oldToNew[src];

            // Zkopirujeme Transform
            var t = _transforms.Get(src); // kopie structu

            // Pro koren podstromu posuneme pozici o kus vedle
            if (src == rootSrc)
            {
                t.Position += new Vector3(0.75f, 0f, 0.75f);
            }
            else
            {
                // Pro potomky premapujeme parenta na novou kopii parenta
                if (t.Parent >= 0 && oldToNew.TryGetValue(t.Parent, out Entity newParent))
                {
                    t.Parent = newParent.Index;
                }
            }
            _world.Add(dest, t);

            // Zkopirujeme MeshRenderer
            if (_renderers.Has(src))
            {
                var r = _renderers.Get(src);
                if (r.ModelHandle >= 0) _assets.AddRef(r.ModelHandle);
                _world.Add(dest, r);
            }

            // Zkopirujeme LightComponent
            if (_lights.Has(src))
            {
                _world.Add(dest, _lights.Get(src));
            }

            // Zkopirujeme BehaviorComponent
            if (_behaviors.Has(src))
            {
                _world.Add(dest, _behaviors.Get(src));
            }

            // Zkopirujeme ParticleEmitter
            if (_emitters.Has(src))
            {
                _world.Add(dest, _emitters.Get(src));
            }

            // Zkopirujeme AudioSourceComponent
            if (_audioSources.Has(src))
            {
                _world.Add(dest, _audioSources.Get(src));
            }

            // Zkopirujeme TriggerComponent
            if (_triggers.Has(src))
            {
                _world.Add(dest, _triggers.Get(src));
            }

            // Zkopirujeme ActionComponent
            if (_actions.Has(src))
            {
                _world.Add(dest, _actions.Get(src));
            }

            // Zkopirujeme Name
            string baseName = _names.Has(src) ? _names.Get(src).Value : $"Entita {src}";
            string newName = src == rootSrc ? $"{baseName} kopie" : baseName;
            _world.Add(dest, new Name { Value = newName });
        }

        // Vybereme novou kopii korene
        _selection.EntityIndex = oldToNew[rootSrc].Index;
        RecordHistoryState();
    }

    /// <summary>
    /// Smaze vybranou entitu. Model handle se musi uvolnit (refcount), jinak zustane viset v pameti.
    /// Deti se NEmazou - prevesi se na prarodice (world poza zustava). Kdyby zustaly
    /// viset na smazanem indexu, po recyklaci indexu by se pripojily k cizi entite.
    /// </summary>
    private void DeleteSelected()
    {
        if (_physics is not null)
        {
            ToastSystem.Show("Během spuštěné fyziky nelze mazat objekty!", 3f);
            return;
        }
        if (!_selection.HasSelection || !_transforms.Has(_selection.EntityIndex)) return;

        int idx = _selection.EntityIndex;
        int grandparent = _transforms.Get(idx).Parent;   // -1, kdyz mazany je root

        var entities = _transforms.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            int e = entities[i];
            if (e != idx && _transforms.Get(e).Parent == idx)
                TransformHierarchy.Reparent(_transforms, e, grandparent);
        }

        if (_renderers.Has(idx))
        {
            ref var r = ref _renderers.Get(idx);
            if (r.ModelHandle >= 0) _assets.Release(r.ModelHandle);
        }

        if (idx == _playerEntity)
        {
            _playerEntity = -1;
            _playerBody = null;
        }

        _world.Destroy(_world.EntityFromIndex(idx));
        _selection.Clear();
        _gizmo.CancelDrag();
        RecordHistoryState();
    }

    /// <summary>
    private int PickEntity(Ray ray)
    {
        float best = float.MaxValue;
        int bestEntity = -1;

        var renderers = _renderers.Span;
        var entities = _renderers.Entities;

        for (int i = 0; i < renderers.Length; i++)
        {
            ref var r = ref renderers[i];
            if (!r.Visible) continue;

            ref var t = ref _transforms.Get(entities[i]);
            RayCollision hit = default;

            if (r.ModelHandle >= 0)
            {
                // Stejna transpozice jako pri kresleni - raylib je column-major.
                var world = Matrix4x4.Transpose(t.World);

                ref Model m = ref _assets.Get(r.ModelHandle);
                hit = default;
                unsafe
                {
                    for (int mi = 0; mi < m.MeshCount; mi++)
                    {
                        var h = Raylib.GetRayCollisionMesh(ray, m.Meshes[mi], world);
                        if (h.Hit && (!hit.Hit || h.Distance < hit.Distance))
                            hit = h;
                    }
                }
            }
            else
            {
                // Rotovaný picking pomocí transformace paprsku do lokálního prostoru
                if (Matrix4x4.Invert(t.World, out var invWorld))
                {
                    Vector3 localStart = Vector3.Transform(ray.Position, invWorld);
                    Vector3 localDir = Vector3.Normalize(Vector3.TransformNormal(ray.Direction, invWorld));
                    var localRay = new Ray(localStart, localDir);
                    
                    var localAABB = new BoundingBox(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f));
                    var localHit = Raylib.GetRayCollisionBox(localRay, localAABB);
                    if (localHit.Hit)
                    {
                        hit.Hit = true;
                        hit.Point = Vector3.Transform(localHit.Point, t.World);
                        hit.Distance = Vector3.Distance(ray.Position, hit.Point);
                        hit.Normal = Vector3.Normalize(Vector3.TransformNormal(localHit.Normal, t.World));
                    }
                    else
                    {
                        hit.Hit = false;
                    }
                }
                else
                {
                    var half = TransformHierarchy.WorldScale(t.World) * 0.5f;
                    var center = t.World.Translation;
                    hit = Raylib.GetRayCollisionBox(ray, new BoundingBox(center - half, center + half));
                }
            }

            if (hit.Hit && hit.Distance < best)
            {
                best = hit.Distance;
                bestEntity = entities[i];
            }
        }

        // Billboard/Helper picking pro entity bez viditelného mesh rendereru
        var transformsSpan = _transforms.Span;
        var trEntities = _transforms.Entities;
        for (int i = 0; i < transformsSpan.Length; i++)
        {
            int idx = trEntities[i];
            bool hasRenderer = _renderers.Has(idx) && _renderers.Get(idx).Visible;
            if (!hasRenderer)
            {
                bool hasAudio = _audioSources.Has(idx);
                bool hasEmitter = _emitters.Has(idx);
                bool isEmpty = !_renderers.Has(idx); // prázdný objekt

                if (hasAudio || hasEmitter || isEmpty)
                {
                    var center = transformsSpan[i].World.Translation;
                    // Test kolize s malou koulí na pozici helperu
                    var hit = Raylib.GetRayCollisionSphere(ray, center, 0.25f);
                    if (hit.Hit && hit.Distance < best)
                    {
                        best = hit.Distance;
                        bestEntity = idx;
                    }
                }
            }
        }

        return bestEntity;
    }

    private void ApplyTextureAtMouse(string relPath)
    {
        Ray ray = _viewport.GetPickRay(_camera.Camera);
        int hitEntity = PickEntity(ray);
        if (hitEntity >= 0)
        {
            if (!_renderers.Has(hitEntity))
            {
                _renderers.Add(hitEntity, new MeshRenderer
                {
                    ModelHandle = -1,
                    Tint = new Vector3(0.8f, 0.8f, 0.8f),
                    Visible = true,
                    AlbedoTexturePath = relPath
                });
            }
            else
            {
                ref var r = ref _renderers.Get(hitEntity);
                r.AlbedoTexturePath = relPath;
            }
            RecordHistoryState();
        }
    }

    private void CreateEmptyEntity()
    {
        if (_physics is not null)
        {
            ToastSystem.Show("Během spuštěné fyziky nelze vytvářet objekty!", 3f);
            return;
        }
        var e = _world.Create();
        var t = Transform.Identity;
        Vector3 camForward = Vector3.Normalize(_camera.Camera.Target - _camera.Camera.Position);
        t.Position = _camera.Camera.Position + camForward * 4f;
        _world.Add(e, t);
        _world.Add(e, new Name { Value = $"Prázdný {e.Index}" });
        _selection.EntityIndex = e.Index;
        RecordHistoryState();
    }

    private void CreateChildOf(int parentIdx)
    {
        if (_physics is not null)
        {
            ToastSystem.Show("Během spuštěné fyziky nelze vytvářet objekty!", 3f);
            return;
        }
        var e = _world.Create();
        var t = Transform.Identity;
        t.Parent = parentIdx;
        _world.Add(e, t);
        _world.Add(e, new Name { Value = $"Prázdný {e.Index}" });
        _selection.EntityIndex = e.Index;
        RecordHistoryState();
    }

    private void RecordHistoryState()
    {
        if (_physics is not null) return;

        var data = SceneSerializer.CreateSceneData(_transforms, _renderers, _names, _emitters, _audioSources, _behaviors, _triggers, _actions, _lights, _assets, _lighting, _viewport.PostProcessor);
        var json = System.Text.Json.JsonSerializer.Serialize(data, SceneJsonContext.Default.SceneData);

        if (_undoStack.Count > 0 && _undoStack[^1] == json) return;

        _undoStack.Add(json);
        if (_undoStack.Count > 30)
            _undoStack.RemoveAt(0);

        _redoStack.Clear();
    }

    private void Undo()
    {
        if (_undoStack.Count <= 1) return;

        var current = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(current);

        var targetJson = _undoStack[^1];
        var data = System.Text.Json.JsonSerializer.Deserialize(targetJson, SceneJsonContext.Default.SceneData);
        if (data is not null)
        {
            SceneSerializer.ApplySceneData(data, _world, _transforms, _renderers, _names, _emitters, _audioSources, _behaviors, _triggers, _actions, _lights, _assets, _lighting, _viewport.PostProcessor);
            _selection.Clear();
            _gizmo.CancelDrag();
            _hierarchy.SceneStatus = "Undo provedeno";
            ToastSystem.Show("Zpět (Undo)");
        }
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;

        var targetJson = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(targetJson);

        var data = System.Text.Json.JsonSerializer.Deserialize(targetJson, SceneJsonContext.Default.SceneData);
        if (data is not null)
        {
            SceneSerializer.ApplySceneData(data, _world, _transforms, _renderers, _names, _emitters, _audioSources, _behaviors, _triggers, _actions, _lights, _assets, _lighting, _viewport.PostProcessor);
            _selection.Clear();
            _gizmo.CancelDrag();
            _hierarchy.SceneStatus = "Redo provedeno";
            ToastSystem.Show("Znovu (Redo)");
        }
    }

    /// <summary>
    /// Vyber entity klikem do viewportu. Paprsek z mysi se testuje proti vsem
    /// viditelnym entitam, nejblizsi zasah vyhrava. Klik do prazdna = odznacit.
    /// </summary>
    private void HandlePicking()
    {
        if (_gizmoActive || _gizmo.Dragging) return;   // tazeni/chyceni sipky neni vyber
        if (!_viewport.Hovered || _viewport.Captured) return;
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

        // Alt+klik je zacatek rozhlizeni, ne vyber.
        if (_altLooking) return;
        if (Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt)) return;

        Ray ray = _viewport.GetPickRay(_camera.Camera);
        _selection.EntityIndex = PickEntity(ray);
    }

    private static void NextWindowRect(Vector2 pos, Vector2 size)
    {
        ImGui.SetNextWindowPos(pos, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(size, ImGuiCond.FirstUseEver);
    }

    private void DrawStatsPanel()
    {
        ImGui.Begin("Stats");
        ImGui.Text($"FPS: {Raylib.GetFPS()}");
        ImGui.Text($"Entity: {_transforms.Count}");
        ImGui.Text($"Viewport: {_viewport.Width}x{_viewport.Height}");
        ImGui.Text($"Kamera: {(_viewport.Captured ? "ZACHYCENA (prava mys)" : "volna")}");

        // Nejuzitecnejsi debug radek v celem enginu.
        // Jakmile tu naskoci nenulove cislo, prave jsi neco alokoval v herni smycce.
        ImGui.Separator();
        ImGui.Text($"Alokace/frame (Update+Render): {_allocPerFrame} B");
        if (_allocPerFrame > 0)
            ImGui.TextColored(new Vector4(1, 0.4f, 0.2f, 1), "^ nekdo alokuje v hot pathu");

        ImGui.End();
    }

    private void CreateDemoScene()
    {
        // Vytvoreni hratelneho Player charakteru
        var pe = _world.Create();
        var pt = Transform.Identity;
        pt.Position = new Vector3(0f, 4f, 0f);
        pt.Scale = new Vector3(0.8f, 1.8f, 0.8f); // vyska postavy cca 1.8m
        _world.Add(pe, pt);
        _world.Add(pe, new MeshRenderer
        {
            ModelHandle = -1,
            Tint = new Vector3(0.1f, 0.8f, 0.2f), // zelena
            Visible = true
        });
        _world.Add(pe, new Name { Value = "Player" });

        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            var e = _world.Create();

            var t = Transform.Identity;
            t.Position = new Vector3(
                (float)(rng.NextDouble() * 40 - 20),
                (float)(rng.NextDouble() * 6),
                (float)(rng.NextDouble() * 40 - 20));
            t.Scale = Vector3.One * (float)(0.5 + rng.NextDouble());
            _world.Add(e, t);

            _world.Add(e, new MeshRenderer
            {
                ModelHandle = -1,
                Tint = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()),
                Visible = true
            });

            _world.Add(e, new Name { Value = $"Krychle {e.Index}" });
        }
    }

    /// <summary>
    /// Nacte assets/models/test.glb, pokud existuje. Cesta se resi od exe
    /// (AppContext.BaseDirectory), ne od pracovniho adresare - 'dotnet run'
    /// i dvojklik na exe pak najdou assety stejne.
    /// </summary>
    private void TryLoadTestModel()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "models", "test.glb");
        if (!File.Exists(path)) return;

        int handle = _assets.LoadModel(path);

        var e = _world.Create();
        var t = Transform.Identity;
        t.Position = new Vector3(0f, 2f, 0f);
        _world.Add(e, t);
        _world.Add(e, new MeshRenderer { ModelHandle = handle, Tint = Vector3.One, Visible = true });
        _world.Add(e, new Name { Value = "Donut (test.glb)" });
    }

    private void RefreshAssetFiles()
    {
        _assetFiles.Clear();
        string assetsDir = Path.Combine(AppContext.BaseDirectory, "assets");
        if (Directory.Exists(assetsDir))
        {
            foreach (var file in Directory.GetFiles(assetsDir, "*.*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(assetsDir, file);
                string ext = Path.GetExtension(file).ToLower();
                if (ext == ".glb" || ext == ".gltf" || ext == ".vs" || ext == ".fs" ||
                    ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tga" ||
                    ext == ".wav" || ext == ".ogg" || ext == ".mp3" || file.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase))
                {
                    _assetFiles.Add(rel);
                }
            }
        }
    }

    private void SpawnModelAssetAt(string relPath, Vector3 worldPos)
    {
        try
        {
            string fullPath = Path.Combine(AppContext.BaseDirectory, "assets", relPath);
            int handle = _assets.LoadModel(fullPath);

            var e = _world.Create();
            var t = Transform.Identity;
            t.Position = worldPos;
            
            _world.Add(e, t);
            _world.Add(e, new MeshRenderer { ModelHandle = handle, Tint = Vector3.One, Visible = true });
            _world.Add(e, new Name { Value = Path.GetFileNameWithoutExtension(relPath) });
            
            _selection.EntityIndex = e.Index;
            _hierarchy.SceneStatus = $"Model přetažen a spawnut: {Path.GetFileName(relPath)}";
            RecordHistoryState();
        }
        catch (Exception ex)
        {
            _hierarchy.SceneStatus = $"Chyba přetažení: {ex.Message}";
        }
    }

    private void SpawnModelAsset(string relPath)
    {
        try
        {
            string fullPath = Path.Combine(AppContext.BaseDirectory, "assets", relPath);
            int handle = _assets.LoadModel(fullPath);

            var e = _world.Create();
            var t = Transform.Identity;
            
            Vector3 camForward = Vector3.Normalize(_camera.Camera.Target - _camera.Camera.Position);
            t.Position = _camera.Camera.Position + camForward * 4f;
            
            _world.Add(e, t);
            _world.Add(e, new MeshRenderer { ModelHandle = handle, Tint = Vector3.One, Visible = true });
            _world.Add(e, new Name { Value = Path.GetFileNameWithoutExtension(relPath) });
            
            _selection.EntityIndex = e.Index;
            _hierarchy.SceneStatus = $"Načten a spawnut model: {Path.GetFileName(relPath)}";
            RecordHistoryState();
        }
        catch (Exception ex)
        {
            _hierarchy.SceneStatus = $"Chyba spawnu: {ex.Message}";
        }
    }

    private void SpawnPrefabAt(string relPath, Vector3 worldPos)
    {
        if (_physics is not null)
        {
            ToastSystem.Show("Během spuštěné fyziky nelze spawnovat prefaby!", 3f);
            return;
        }
        try
        {
            string fullPath = Path.Combine(AppContext.BaseDirectory, "assets", relPath);
            var json = File.ReadAllText(fullPath);
            var prefabData = System.Text.Json.JsonSerializer.Deserialize(json, SceneJsonContext.Default.SceneData);
            if (prefabData != null)
            {
                int newRoot = SceneSerializer.ApplyPrefabData(prefabData, worldPos, _world, _transforms, _renderers, _names, _emitters, _audioSources, _behaviors, _triggers, _actions, _lights, _assets);
                _selection.EntityIndex = newRoot;
                _hierarchy.SceneStatus = $"Prefab přetažen a spawnut: {Path.GetFileName(relPath)}";
                RecordHistoryState();
            }
        }
        catch (Exception ex)
        {
            _hierarchy.SceneStatus = $"Chyba přetažení prefabu: {ex.Message}";
        }
    }

    private void SpawnPrefab(string relPath)
    {
        if (_physics is not null)
        {
            ToastSystem.Show("Během spuštěné fyziky nelze spawnovat prefaby!", 3f);
            return;
        }
        try
        {
            string fullPath = Path.Combine(AppContext.BaseDirectory, "assets", relPath);
            var json = File.ReadAllText(fullPath);
            var prefabData = System.Text.Json.JsonSerializer.Deserialize(json, SceneJsonContext.Default.SceneData);
            if (prefabData != null)
            {
                Vector3 camForward = Vector3.Normalize(_camera.Camera.Target - _camera.Camera.Position);
                Vector3 spawnPos = _camera.Camera.Position + camForward * 4f;
                int newRoot = SceneSerializer.ApplyPrefabData(prefabData, spawnPos, _world, _transforms, _renderers, _names, _emitters, _audioSources, _behaviors, _triggers, _actions, _lights, _assets);
                _selection.EntityIndex = newRoot;
                _hierarchy.SceneStatus = $"Načten a spawnut prefab: {Path.GetFileName(relPath)}";
                RecordHistoryState();
            }
        }
        catch (Exception ex)
        {
            _hierarchy.SceneStatus = $"Chyba spawnu prefabu: {ex.Message}";
        }
    }

    private void SaveSelectedPrefab()
    {
        if (!_selection.HasSelection || !_transforms.Has(_selection.EntityIndex)) return;

        int rootIdx = _selection.EntityIndex;
        string entName = _names.Has(rootIdx) ? _names.Get(rootIdx).Value : $"Entity_{rootIdx}";

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            entName = entName.Replace(c, '_');
        }
        entName = entName.Replace(' ', '_');

        string prefabsDir = Path.Combine(AppContext.BaseDirectory, "assets", "prefabs");
        if (!Directory.Exists(prefabsDir)) Directory.CreateDirectory(prefabsDir);

        string filename = $"{entName}.prefab.json";
        string fullPath = Path.Combine(prefabsDir, filename);

        try
        {
            var prefabData = SceneSerializer.CreatePrefabData(rootIdx, _transforms, _renderers, _names, _emitters, _audioSources, _behaviors, _triggers, _actions, _lights, _assets);
            var json = System.Text.Json.JsonSerializer.Serialize(prefabData, SceneJsonContext.Default.SceneData);
            File.WriteAllText(fullPath, json);
            _hierarchy.SceneStatus = $"Prefab uložen: prefabs/{filename}";
            RefreshAssetFiles();
        }
        catch (Exception ex)
        {
            _hierarchy.SceneStatus = $"Chyba uložení prefabu: {ex.Message}";
        }
    }

    private void UpdateTriggers()
    {
        var triggersSpan = _triggers.Span;
        var triggerEntities = _triggers.Entities;
        Vector3 camPos = _camera.Camera.Position;

        for (int i = 0; i < triggersSpan.Length; i++)
        {
            ref var trigger = ref triggersSpan[i];
            if (!trigger.Active) continue;

            int triggerEnt = triggerEntities[i];
            if (!_transforms.Has(triggerEnt)) continue;

            Vector3 triggerPos = _transforms.Get(triggerEnt).World.Translation;
            Vector3 min = triggerPos - trigger.Size * 0.5f;
            Vector3 max = triggerPos + trigger.Size * 0.5f;

            // Kontrola kamery
            bool isCameraInside =
                camPos.X >= min.X && camPos.X <= max.X &&
                camPos.Y >= min.Y && camPos.Y <= max.Y &&
                camPos.Z >= min.Z && camPos.Z <= max.Z;

            // Kontrola ostatních objektů ve scéně
            bool isAnyOtherEntityInside = false;
            var transformsSpan = _transforms.Span;
            var transformEntities = _transforms.Entities;
            for (int j = 0; j < transformsSpan.Length; j++)
            {
                int otherEnt = transformEntities[j];
                if (otherEnt == triggerEnt) continue;

                // Nepočítat přímé děti/rodiče triggeru, abychom se netriggerovali sami
                if (_transforms.Get(otherEnt).Parent == triggerEnt || _transforms.Get(triggerEnt).Parent == otherEnt)
                    continue;

                Vector3 otherPos = transformsSpan[j].World.Translation;
                if (otherPos.X >= min.X && otherPos.X <= max.X &&
                    otherPos.Y >= min.Y && otherPos.Y <= max.Y &&
                    otherPos.Z >= min.Z && otherPos.Z <= max.Z)
                {
                    isAnyOtherEntityInside = true;
                    break;
                }
            }

            bool currentlyTriggered = isCameraInside || isAnyOtherEntityInside;
            if (currentlyTriggered && !trigger.WasTriggered)
            {
                // Vstup do zóny -> Vyvolat akci
                if (trigger.TargetEntity >= 0 && _actions.Has(trigger.TargetEntity))
                {
                    ref var action = ref _actions.Get(trigger.TargetEntity);
                    if (action.Active)
                    {
                        ExecuteAction(trigger.TargetEntity, ref action);
                    }
                }
            }
            trigger.WasTriggered = currentlyTriggered;
        }
    }

    private void ExecuteAction(int targetEntity, ref ActionComponent action)
    {
        if (action.ActionType == 0) // Spustit zvuk
        {
            if (_audioSources.Has(targetEntity))
            {
                ref var src = ref _audioSources.Get(targetEntity);
                src.Active = true;
            }
            else if (!string.IsNullOrEmpty(action.ActionParam))
            {
                var sound = _assets.GetSound(action.ActionParam);
                Raylib.PlaySound(sound);
            }
        }
        else if (action.ActionType == 1) // Přepnout částice
        {
            if (_emitters.Has(targetEntity))
            {
                ref var em = ref _emitters.Get(targetEntity);
                em.Active = !em.Active;
            }
        }
        else if (action.ActionType == 2) // Přepnout viditelnost
        {
            if (_renderers.Has(targetEntity))
            {
                ref var rend = ref _renderers.Get(targetEntity);
                rend.Visible = !rend.Visible;
            }
        }
    }

    private unsafe void DrawAssetBrowserPanel()
    {
        ImGui.Begin("Asset Browser");

        if (ImGui.Button("Obnovit")) RefreshAssetFiles();
        ImGui.SameLine();
        ImGui.TextDisabled("Kliknutím na model/prefab jej spawneš. Táhnutím ho přetáhneš do scény.");

        ImGui.InputText("Hledat", ref _assetSearchQuery, 128);

        string[] filters = ["Vše", "Modely (*.glb/*.gltf)", "Textury (*.png/*.jpg...)", "Zvuky (*.wav/*.ogg...)", "Shadery (*.vs/*.fs)", "Prefaby (*.prefab.json)"];
        ImGui.Combo("Filtr typů", ref _assetFilterType, filters, filters.Length);

        ImGui.Separator();

        if (ImGui.BeginChild("FilesList"))
        {
            float panelWidth = ImGui.GetContentRegionAvail().X;
            int columns = Math.Max(1, (int)(panelWidth / 115f));

            if (ImGui.BeginTable("AssetBrowserTable", columns))
            {
                foreach (var relPath in _assetFiles)
                {
                    string ext = Path.GetExtension(relPath).ToLower();
                    
                    if (!string.IsNullOrEmpty(_assetSearchQuery) && relPath.IndexOf(_assetSearchQuery, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    bool matchesFilter = _assetFilterType switch
                    {
                        0 => true,
                        1 => ext == ".glb" || ext == ".gltf",
                        2 => ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tga",
                        3 => ext == ".wav" || ext == ".ogg" || ext == ".mp3",
                        4 => ext == ".vs" || ext == ".fs",
                        5 => relPath.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase),
                        _ => true
                    };

                    if (!matchesFilter) continue;

                    ImGui.TableNextColumn();
                    ImGui.PushID(relPath);

                    string icon = "📄";
                    Vector4 color = new Vector4(0.5f, 0.5f, 0.5f, 1f); // default gray

                    if (ext == ".glb" || ext == ".gltf")
                    {
                        icon = "📦";
                        color = new Vector4(0.24f, 0.42f, 0.75f, 1f); // Blue
                    }
                    else if (ext == ".vs" || ext == ".fs")
                    {
                        icon = "⚙️";
                        color = new Vector4(0.4f, 0.4f, 0.45f, 1f); // Gray
                    }
                    else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tga")
                    {
                        icon = "🖼️";
                        color = new Vector4(0.24f, 0.70f, 0.42f, 1f); // Green
                    }
                    else if (ext == ".wav" || ext == ".ogg" || ext == ".mp3")
                    {
                        icon = "🎵";
                        color = new Vector4(0.6f, 0.3f, 0.8f, 1f); // Purple
                    }
                    else if (relPath.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase))
                    {
                        icon = "🧩";
                        color = new Vector4(0.85f, 0.5f, 0.2f, 1f); // Orange
                    }

                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(color.X, color.Y, color.Z, 0.15f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(color.X, color.Y, color.Z, 0.30f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(color.X, color.Y, color.Z, 0.50f));
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);

                    string filename = Path.GetFileName(relPath);
                    string displayLabel = filename;
                    if (displayLabel.Length > 12) displayLabel = displayLabel.Substring(0, 10) + "..";

                    ImGui.BeginGroup();
                    if (ImGui.Button($"{icon}\n{displayLabel}", new Vector2(100f, 75f)))
                    {
                        if (ext == ".glb" || ext == ".gltf")
                        {
                            SpawnModelAsset(relPath);
                        }
                        else if (relPath.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase))
                        {
                            SpawnPrefab(relPath);
                        }
                    }

                    bool isModel = ext == ".glb" || ext == ".gltf";
                    bool isTexture = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tga";
                    bool isAudio = ext == ".wav" || ext == ".ogg" || ext == ".mp3";
                    bool isPrefab = relPath.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase);

                    if (isModel || isTexture || isAudio || isPrefab)
                    {
                        if (ImGui.BeginDragDropSource())
                        {
                            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(relPath);
                            fixed (byte* p = bytes)
                            {
                                string payloadName = isModel ? "ASSET_MODEL_PATH" : 
                                                     (isTexture ? "ASSET_TEXTURE_PATH" : 
                                                     (isAudio ? "ASSET_AUDIO_PATH" : "ASSET_PREFAB_PATH"));
                                ImGui.SetDragDropPayload(payloadName, (IntPtr)p, (uint)bytes.Length);
                            }
                            ImGui.Text($"Táhnutí: {filename}");
                            ImGui.EndDragDropSource();
                        }
                    }
                    ImGui.EndGroup();

                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(3);

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"Název: {filename}");
                        ImGui.Text($"Typ: {ext.ToUpper()}");
                        ImGui.Text($"Cesta: assets/{relPath}");
                        ImGui.EndTooltip();
                    }

                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            ImGui.EndChild();
        }

        ImGui.End();
    }

    public void Dispose() { }
}
