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
    private readonly ParticleSystem _particleSystem = new();
    private readonly AudioSystem _audioSystem = new();
    private readonly BehaviorSystem _behaviorSystem = new();
    private SkyboxRenderer _skybox = null!;

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
    private HierarchyPanel _hierarchy = null!;
    private InspectorPanel _inspector = null!;
    private LightPanel _lightPanel = null!;

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
    }

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1600, 900, "MiniEngine");
        Raylib.InitAudioDevice();
        Raylib.SetTargetFPS(0);   // VSync ridi tempo

        rlImGui.Setup(darkTheme: true, enableDocking: true);
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
        _lightPanel = new LightPanel(_lighting);
        _drawSceneCached = DrawScene;
        _viewport.DrawToolbar = DrawViewportToolbar;   // jednou, ne per frame (delegat)
        _hierarchy.OnSave = SaveScene;
        _hierarchy.OnLoad = LoadScene;
        _hierarchy.OnDuplicate = DuplicateSelected;
        _hierarchy.OnDelete = DeleteSelected;
        _hierarchy.OnReparent = ReparentEntity;

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
                }
                else
                {
                    _hierarchy.SceneStatus = "Chyba reloadu shaderu (GLSL syntax error?)";
                }
            }
            else
            {
                _hierarchy.SceneStatus = $"Model hot-reloaded: {Path.GetFileName(path)}";
            }
        };

        RefreshAssetFiles();
        CreateDemoScene();
        TryLoadTestModel();   // az PO InitWindow - LoadModel nahrava na GPU

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
            _viewport.DrawPanel();
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

            NextWindowRect(new Vector2(wp.X, wp.Y + ws.Y * 0.58f), new Vector2(leftW, ws.Y * 0.42f));
            DrawStatsPanel();

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

            if (Raylib.IsKeyPressed(KeyboardKey.R)) _camera.Reset();
            if (Raylib.IsKeyPressed(KeyboardKey.F)) FocusSelection();
            if (cmdCtrl && Raylib.IsKeyPressed(KeyboardKey.D)) DuplicateSelected();

            // Rezim gizma jako v Unity, jen na cislicich (pismena zabira pohyb kamery).
            if (Raylib.IsKeyPressed(KeyboardKey.One)) _gizmo.SetMode(GizmoMode.Translate);
            if (Raylib.IsKeyPressed(KeyboardKey.Two)) _gizmo.SetMode(GizmoMode.Rotate);
            if (Raylib.IsKeyPressed(KeyboardKey.Three)) _gizmo.SetMode(GizmoMode.Scale);
        }

        // Fyzika: fixed timestep + interpolace, tela jsou zdroj pravdy pro Transform.
        // Fyzika mysli ve WORLD prostoru - u deti v hierarchii se poza prepocita
        // na lokalni pres rodicovu World matici (z minuleho framu, to je ok).
        if (_physics is not null)
        {
            if (_playerBody.HasValue)
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
            SceneSerializer.Save(ScenePath, _transforms, _renderers, _names, _emitters, _audioSources, _behaviors, _assets, _lighting);
            _hierarchy.SceneStatus = "Ulozeno: scene.json";
        }
        catch (Exception ex)
        {
            _hierarchy.SceneStatus = $"Chyba ukladani: {ex.Message}";
        }
    }

    private void LoadScene()
    {
        if (!File.Exists(ScenePath))
        {
            _hierarchy.SceneStatus = "scene.json neexistuje - nejdriv uloz";
            return;
        }

        StopPhysics();   // nacitani za behu fyziky = tela by ukazovala na smazane entity

        try
        {
            SceneSerializer.Load(ScenePath, _world, _transforms, _renderers, _names, _emitters, _audioSources, _behaviors, _assets, _lighting);
            _lightPanel.LoadFrom(_lighting.SunDirection);
            _selection.Clear();
            _hierarchy.SceneStatus = "Nacteno: scene.json";
        }
        catch (Exception ex)
        {
            _hierarchy.SceneStatus = $"Chyba nacitani: {ex.Message}";
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
    }

    private void DrawViewportToolbar()
    {
        GizmoModeButton("Posun (1)", GizmoMode.Translate);
        ImGui.SameLine();
        GizmoModeButton("Rotace (2)", GizmoMode.Rotate);
        ImGui.SameLine();
        GizmoModeButton("Meritko (3)", GizmoMode.Scale);

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        if (_physics is null)
        {
            if (ImGui.Button("Fyzika: START")) StartPhysics();
        }
        else
        {
            if (ImGui.Button("Fyzika: STOP")) StopPhysics();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset kamery (R)"))
            _camera.Reset();

        ImGui.SameLine();
        if (ImGui.Button("Na vyber (F)"))
            FocusSelection();

        ImGui.SameLine();
        ImGui.TextDisabled("1/2/3 = rezim gizma | WASD/sipky = pohyb (Shift rychleji) | Alt+tazeni = rozhlizeni | kolecko = zoom | klik = vyber | Cmd = krok | Cmd+D = duplikat");
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
                        Color origColor = mat.Maps[(int)MaterialMapIndex.Albedo].Color;
                        Texture2D origTex = mat.Maps[(int)MaterialMapIndex.Albedo].Texture;

                        byte rVal = (byte)Math.Clamp(origColor.R * r.Tint.X, 0f, 255f);
                        byte gVal = (byte)Math.Clamp(origColor.G * r.Tint.Y, 0f, 255f);
                        byte bVal = (byte)Math.Clamp(origColor.B * r.Tint.Z, 0f, 255f);
                        mat.Maps[(int)MaterialMapIndex.Albedo].Color = new Color(rVal, gVal, bVal, origColor.A);

                        if (texOverride.Id > 0)
                        {
                            mat.Maps[(int)MaterialMapIndex.Albedo].Texture = texOverride;
                        }

                        Raylib.DrawMesh(m.Meshes[mi], mat, world);

                        mat.Maps[(int)MaterialMapIndex.Albedo].Color = origColor;
                        mat.Maps[(int)MaterialMapIndex.Albedo].Texture = origTex;
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

                    if (texOverride.Id > 0)
                    {
                        _cubeMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = texOverride;
                    }

                    Raylib.DrawMesh(_cubeMesh, _cubeMaterial, Matrix4x4.Transpose(t.World));

                    _cubeMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = origTex;
                }
            }
        }
    }

    private void DrawScene(Camera3D camera)
    {
        _skybox.Draw(camera);

        _lighting.Apply(camera);   // viewPos + parametry slunce do shaderu

        Raylib.DrawGrid(40, 1f);

        DrawSceneGeometries();

        _particleSystem.Draw();

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
            return;
        }

        bool altDown = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
        bool canStartDrag = _viewport.Hovered && !_viewport.Captured && !_altLooking && !altDown;

        _gizmoActive = _gizmo.UpdateInteraction(_transforms, _selection.EntityIndex,
            _viewport.GetPickRay(_camera.Camera), _camera.Camera, canStartDrag);

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

            // Zkopirujeme Name
            string baseName = _names.Has(src) ? _names.Get(src).Value : $"Entita {src}";
            string newName = src == rootSrc ? $"{baseName} kopie" : baseName;
            _world.Add(dest, new Name { Value = newName });
        }

        // Vybereme novou kopii korene
        _selection.EntityIndex = oldToNew[rootSrc].Index;
    }

    /// <summary>
    /// Smaze vybranou entitu. Model handle se musi uvolnit (refcount), jinak zustane viset v pameti.
    /// Deti se NEmazou - prevesi se na prarodice (world poza zustava). Kdyby zustaly
    /// viset na smazanem indexu, po recyklaci indexu by se pripojily k cizi entite.
    /// </summary>
    private void DeleteSelected()
    {
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

        _world.Destroy(_world.EntityFromIndex(idx));
        _selection.Clear();
        _gizmo.CancelDrag();
    }

    /// <summary>
    /// Vyber entity klikem do viewportu. Paprsek z mysi se testuje proti vsem
    /// viditelnym entitam, nejblizsi zasah vyhrava. Klik do prazdna = odznacit.
    /// U placeholder krychli staci AABB (BoundingBox), u modelu presny test
    /// proti trojuhelnikum (GetRayCollisionMesh) - klik na diru donutu neproleze.
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

        float best = float.MaxValue;
        int bestEntity = -1;

        var renderers = _renderers.Span;
        var entities = _renderers.Entities;

        for (int i = 0; i < renderers.Length; i++)
        {
            ref var r = ref renderers[i];
            if (!r.Visible) continue;

            ref var t = ref _transforms.Get(entities[i]);
            RayCollision hit;

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
                // AABB kolem krychle z WORLD pozy (dite ma v Position lokalni souradnice).
                // Rotaci ignoruje - pro placeholder OK.
                var half = TransformHierarchy.WorldScale(t.World) * 0.5f;
                var center = t.World.Translation;
                hit = Raylib.GetRayCollisionBox(ray, new BoundingBox(center - half, center + half));
            }

            if (hit.Hit && hit.Distance < best)
            {
                best = hit.Distance;
                bestEntity = entities[i];
            }
        }

        _selection.EntityIndex = bestEntity;
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
                if (ext == ".glb" || ext == ".gltf" || ext == ".vs" || ext == ".fs" || ext == ".png")
                {
                    _assetFiles.Add(rel);
                }
            }
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
        }
        catch (Exception ex)
        {
            _hierarchy.SceneStatus = $"Chyba spawnu: {ex.Message}";
        }
    }

    private void DrawAssetBrowserPanel()
    {
        ImGui.Begin("Asset Browser");

        if (ImGui.Button("Obnovit")) RefreshAssetFiles();
        ImGui.SameLine();
        ImGui.TextDisabled("Kliknutím na model (.glb) jej spawneš do scény.");

        ImGui.Separator();

        if (ImGui.BeginChild("FilesList"))
        {
            foreach (var relPath in _assetFiles)
            {
                string ext = Path.GetExtension(relPath).ToLower();
                
                string icon = "📄";
                if (ext == ".glb" || ext == ".gltf") icon = "📦";
                else if (ext == ".vs" || ext == ".fs") icon = "⚙️";
                else if (ext == ".png") icon = "🖼️";

                if (ImGui.Button($"{icon} {relPath}"))
                {
                    if (ext == ".glb" || ext == ".gltf")
                    {
                        SpawnModelAsset(relPath);
                    }
                }
            }
            ImGui.EndChild();
        }

        ImGui.End();
    }

    public void Dispose() { }
}
