using System.Numerics;
using BepuPhysics;
using MiniEngine.Assets;
using MiniEngine.Core;
using MiniEngine.Physics;
using MiniEngine.Rendering;
using MiniEngine.Audio;
using Raylib_cs;
using Transform = MiniEngine.Core.Transform;

namespace MiniEngine.Engine;

// ============================================================
// PlayLoop – simulační část Play módu vytažená z Game.cs
// (Fáze 40, krok 1 refaktoru). Sdílí ji editor i MiniPlayer.
//
// Obsahuje: start/stop fyziky, character controller, fixed-step
// simulaci s interpolací, behavior systém, behavior grafy,
// trigger zóny + akce, částice a 3D audio.
// V Game.cs zůstává: kamera editoru, zkratky, gizmo, picking,
// undo/redo, ImGui panely.
// ============================================================

public sealed class PlayLoop
{
    private readonly World _world;
    private readonly AssetManager _assets;
    private readonly Store<Transform> _transforms;
    private readonly Store<MeshRenderer> _renderers;
    private readonly Store<Name> _names;
    private readonly Store<RigidBodyRef> _bodies;
    private readonly Store<ParticleEmitter> _emitters;
    private readonly Store<AudioSourceComponent> _audioSources;
    private readonly Store<BehaviorComponent> _behaviors;
    private readonly Store<TriggerComponent> _triggers;
    private readonly Store<ActionComponent> _actions;
    private readonly Store<BehaviorGraphComponent> _graphs;

    private readonly ParticleSystem _particleSystem;
    private readonly AudioSystem _audioSystem;
    private readonly BehaviorSystem _behaviorSystem;
    private readonly Dictionary<int, BehaviorGraph> _activeGraphs = new();

    public PhysicsWorld? Physics { get; private set; }
    public bool IsRunning => Physics != null;

    public BodyHandle? PlayerBody { get; private set; }
    public int PlayerEntity { get; private set; } = -1;

    /// <summary>Jméno entity hráče (editor: "Player", MiniPlayer: z game.json).</summary>
    public string PlayerName = "Player";

    /// <summary>Editor sem věší _inspector.InvalidateRotationCache(); přehrávač nic.</summary>
    public Action? OnAfterPhysicsWriteback;

    public PlayLoop(World world, AssetManager assets,
        ParticleSystem particles, AudioSystem audio, BehaviorSystem behaviors)
    {
        _world = world;
        _assets = assets;
        _particleSystem = particles;
        _audioSystem = audio;
        _behaviorSystem = behaviors;

        _transforms = world.Store<Transform>();
        _renderers = world.Store<MeshRenderer>();
        _names = world.Store<Name>();
        _bodies = world.Store<RigidBodyRef>();
        _emitters = world.Store<ParticleEmitter>();
        _audioSources = world.Store<AudioSourceComponent>();
        _behaviors = world.Store<BehaviorComponent>();
        _triggers = world.Store<TriggerComponent>();
        _actions = world.Store<ActionComponent>();
        _graphs = world.Store<BehaviorGraphComponent>();

        // Uvolnění těles ze simulace při odebrání komponenty (dřív v Game ctor)
        _bodies.OnRemoved = (idx, r) =>
        {
            Physics?.Untrack(new BodyHandle(r.BodyHandle));
        };
    }

    // ---------- Start / Stop (dřív Game.StartPhysics / StopPhysics) ----------

    public void Start()
    {
        Physics = new PhysicsWorld();

        // Neviditelná podlaha v úrovni gridu, ať je kam padat.
        Physics.AddStaticBox(new Vector3(0f, -0.25f, 0f), new Vector3(400f, 0.5f, 400f));

        int playerIdx = -1;
        var names = _names.Span;
        var nameEntities = _names.Entities;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i].Value, PlayerName, StringComparison.OrdinalIgnoreCase))
            {
                playerIdx = nameEntities[i];
                break;
            }
        }

        var renderers = _renderers.Span;
        var entities = _renderers.Entities;
        for (int i = 0; i < renderers.Length; i++)
        {
            ref var r = ref renderers[i];
            if (!r.Visible) continue;

            int idx = entities[i];
            if (!_transforms.Has(idx)) continue;

            ref var t = ref _transforms.Get(idx);
            var worldScale = TransformHierarchy.WorldScale(t.World);
            float mass = MathF.Max(0.1f, worldScale.X * worldScale.Y * worldScale.Z);

            if (idx == playerIdx)
            {
                var handle = Physics.AddCharacter(t.World.Translation, 0.4f, 1.0f, mass);
                _bodies.Add(idx, new RigidBodyRef { BodyHandle = handle.Value, IsKinematic = false, CenterOffset = Vector3.Zero });
                PlayerBody = handle;
                PlayerEntity = idx;
            }
            else if (r.ModelHandle >= 0)
            {
                ref Model m = ref _assets.Get(r.ModelHandle);
                var vertexList = new List<Vector3>();
                unsafe
                {
                    for (int mi = 0; mi < m.MeshCount; mi++)
                    {
                        ref var mesh = ref m.Meshes[mi];
                        if (mesh.Vertices != null && mesh.VertexCount > 0)
                        {
                            for (int vi = 0; vi < mesh.VertexCount; vi++)
                            {
                                vertexList.Add(new Vector3(
                                    mesh.Vertices[vi * 3] * worldScale.X,
                                    mesh.Vertices[vi * 3 + 1] * worldScale.Y,
                                    mesh.Vertices[vi * 3 + 2] * worldScale.Z));
                            }
                        }
                    }
                }

                if (vertexList.Count > 0)
                {
                    var handle = Physics.AddDynamicConvexHull(t.World.Translation,
                        TransformHierarchy.WorldRotation(_transforms, idx),
                        vertexList.ToArray(), mass, out var center);
                    _bodies.Add(idx, new RigidBodyRef { BodyHandle = handle.Value, IsKinematic = false, CenterOffset = center });
                }
                else
                {
                    var handle = Physics.AddDynamicBox(t.World.Translation,
                        TransformHierarchy.WorldRotation(_transforms, idx), worldScale, mass);
                    _bodies.Add(idx, new RigidBodyRef { BodyHandle = handle.Value, IsKinematic = false, CenterOffset = Vector3.Zero });
                }
            }
            else
            {
                var handle = Physics.AddDynamicBox(t.World.Translation,
                    TransformHierarchy.WorldRotation(_transforms, idx), worldScale, mass);
                _bodies.Add(idx, new RigidBodyRef { BodyHandle = handle.Value, IsKinematic = false, CenterOffset = Vector3.Zero });
            }
        }

        // Aktivace behavior grafů
        _activeGraphs.Clear();
        var graphEntities = _graphs.Entities;
        var graphSpan = _graphs.Span;
        for (int i = 0; i < graphSpan.Length; i++)
        {
            ref var comp = ref graphSpan[i];
            if (comp.Enabled && !string.IsNullOrEmpty(comp.GraphJson))
            {
                try
                {
                    var g = System.Text.Json.JsonSerializer.Deserialize(comp.GraphJson, SceneJsonContext.Default.BehaviorGraph);
                    if (g != null)
                    {
                        int ent = graphEntities[i];
                        g.InitializeRuntime(_world, ent, Physics);
                        _activeGraphs[ent] = g;
                        g.Trigger("Event_OnStart", _world, ent, Physics);
                    }
                }
                catch { }
            }
        }
    }

    public void Stop()
    {
        Physics?.Dispose();
        Physics = null;
        _bodies.Clear();   // Transformy zůstanou tam, kde tělesa dopadla
        PlayerBody = null;
        PlayerEntity = -1;
        _audioSystem.StopAll(_audioSources, _assets);
        _behaviorSystem.RestoreInitialStates(_transforms, _behaviors);
        _activeGraphs.Clear();
    }

    // ---------- Per-frame simulace (dřív část Game.Update) ----------

    /// <summary>
    /// Volat každý frame (i mimo Play – behavior/částice/audio běží v editoru taky).
    /// camera: pro audio posluchače, trigger zóny a follow hráče (controller ji posouvá).
    /// inputAllowed: editor předá EditorViewport.GameKeyboardAllowed, přehrávač true.
    /// </summary>
    public void Update(float dt, ref Camera3D camera, bool inputAllowed)
    {
        _behaviorSystem.Update(dt, _transforms, _behaviors, _bodies, Physics);
        _particleSystem.Update(dt, _transforms, _emitters);
        _audioSystem.Update(camera, _transforms, _audioSources, _assets);
        UpdateTriggers(camera.Position);

        if (Physics is not null)
        {
            foreach (var kvp in _activeGraphs)
                kvp.Value.Trigger("Event_OnUpdate", _world, kvp.Key, Physics);

            // Character controller (WASD vůči kameře + skok) a follow kamera
            if (inputAllowed && PlayerBody.HasValue && Physics.Simulation.Bodies.BodyExists(PlayerBody.Value))
            {
                var bodyRef = Physics.Simulation.Bodies[PlayerBody.Value];
                Vector3 currentVel = bodyRef.Velocity.Linear;

                Vector3 camDir = new(camera.Target.X - camera.Position.X, 0f, camera.Target.Z - camera.Position.Z);
                Vector3 camForward = camDir.LengthSquared() > 0.001f ? Vector3.Normalize(camDir) : new Vector3(0f, 0f, -1f);
                Vector3 camRight = Vector3.Normalize(Vector3.Cross(camForward, camera.Up));

                Vector3 moveDir = Vector3.Zero;
                if (Raylib.IsKeyDown(KeyboardKey.W)) moveDir += camForward;
                if (Raylib.IsKeyDown(KeyboardKey.S)) moveDir -= camForward;
                if (Raylib.IsKeyDown(KeyboardKey.D)) moveDir += camRight;
                if (Raylib.IsKeyDown(KeyboardKey.A)) moveDir -= camRight;
                if (moveDir != Vector3.Zero) moveDir = Vector3.Normalize(moveDir);

                bodyRef.Velocity.Linear = new Vector3(moveDir.X * 8f, currentVel.Y, moveDir.Z * 8f);

                if (Raylib.IsKeyPressed(KeyboardKey.Space) && MathF.Abs(currentVel.Y) < 0.1f)
                    bodyRef.Velocity.Linear = new Vector3(bodyRef.Velocity.Linear.X, 6f, bodyRef.Velocity.Linear.Z);

                Vector3 playerPos = bodyRef.Pose.Position;
                Vector3 camOffset = camera.Position - camera.Target;
                camera.Target = playerPos;
                camera.Position = playerPos + camOffset;
            }

            // Fixed timestep + interpolace, tělesa jsou zdroj pravdy pro Transform
            float alpha = Physics.Step(dt);
            var bodies = _bodies.Span;
            var bodyEntities = _bodies.Entities;
            for (int i = 0; i < bodies.Length; i++)
            {
                ref var b = ref bodies[i];
                var (pos, rot) = Physics.GetInterpolated(new BodyHandle(b.BodyHandle), alpha);
                Vector3 visualPos = pos - Vector3.Transform(b.CenterOffset, rot);
                TransformHierarchy.SetWorldPosition(_transforms, bodyEntities[i], visualPos);
                TransformHierarchy.SetWorldRotation(_transforms, bodyEntities[i], rot);
            }

            OnAfterPhysicsWriteback?.Invoke();
        }
    }

    // ---------- Trigger zóny + akce (dřív Game.UpdateTriggers / ExecuteAction) ----------

    private void UpdateTriggers(Vector3 camPos)
    {
        var triggersSpan = _triggers.Span;
        var triggerEntities = _triggers.Entities;

        for (int i = 0; i < triggersSpan.Length; i++)
        {
            ref var trigger = ref triggersSpan[i];
            if (!trigger.Active) continue;

            int triggerEnt = triggerEntities[i];
            if (!_transforms.Has(triggerEnt)) continue;

            Vector3 triggerPos = _transforms.Get(triggerEnt).World.Translation;
            Vector3 min = triggerPos - trigger.Size * 0.5f;
            Vector3 max = triggerPos + trigger.Size * 0.5f;

            bool isCameraInside =
                camPos.X >= min.X && camPos.X <= max.X &&
                camPos.Y >= min.Y && camPos.Y <= max.Y &&
                camPos.Z >= min.Z && camPos.Z <= max.Z;

            bool isAnyOtherEntityInside = false;
            var transformsSpan = _transforms.Span;
            var transformEntities = _transforms.Entities;
            for (int j = 0; j < transformsSpan.Length; j++)
            {
                int otherEnt = transformEntities[j];
                if (otherEnt == triggerEnt) continue;
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
                if (_activeGraphs.TryGetValue(triggerEnt, out var gTrigger))
                    gTrigger.Trigger("Event_OnCollision", _world, triggerEnt, Physics, trigger.TargetEntity);
                if (trigger.TargetEntity >= 0 && _activeGraphs.TryGetValue(trigger.TargetEntity, out var gTarget))
                    gTarget.Trigger("Event_OnCollision", _world, trigger.TargetEntity, Physics, triggerEnt);

                if (trigger.TargetEntity >= 0 && _actions.Has(trigger.TargetEntity))
                {
                    ref var action = ref _actions.Get(trigger.TargetEntity);
                    if (action.Active) ExecuteAction(trigger.TargetEntity, ref action);
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
}
