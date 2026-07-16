using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

namespace MiniEngine.Physics;

/// <summary>
/// Bepu vyzaduje dve struktury s callbacky. Vsechno je staticke generikum
/// -> zadna reflexe -> NativeAOT bez problemu. Napises je jednou a uz na ne nesahnes.
/// </summary>
public struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public SpringSettings ContactSpringiness;

    public void Initialize(Simulation simulation)
    {
        if (ContactSpringiness.AngularFrequency == 0)
            ContactSpringiness = new SpringSettings(30, 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        => a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold,
        out PairMaterialProperties material) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        material.FrictionCoefficient = 1f;
        material.MaximumRecoveryVelocity = 2f;
        material.SpringSettings = ContactSpringiness;

        // POZOR: pokud pouzivas Bepu character controller z Demos,
        // MUSIS tady zavolat characters.TryReportContacts(...), jinak charakter propadne podlahou.
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB,
        ref ConvexContactManifold manifold) => true;

    public void Dispose() { }
}

public struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public Vector3 Gravity;
    private Vector3Wide _gravityWideDt;
    private Vector<float> _linearDamping;
    private Vector<float> _angularDamping;

    public float LinearDamping;
    public float AngularDamping;

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;

    public void Initialize(Simulation simulation) { }

    public void PrepareForIntegration(float dt)
    {
        _gravityWideDt = Vector3Wide.Broadcast(Gravity * dt);
        _linearDamping = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - LinearDamping, 0, 1), dt));
        _angularDamping = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - AngularDamping, 0, 1), dt));
    }

    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
        BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        velocity.Linear = (velocity.Linear + _gravityWideDt) * _linearDamping;
        velocity.Angular *= _angularDamping;
    }
}

public sealed class PhysicsWorld : IDisposable
{
    public const float FixedDt = 1f / 60f;

    private readonly BufferPool _pool = new();          // unmanaged pamet -> nulovy GC tlak
    private readonly ThreadDispatcher _dispatcher;
    public readonly Simulation Simulation;

    private float _accumulator;

    // Predchozi pozy pro interpolaci renderu mezi fyzikalnimi kroky.
    // Paralelni pole misto Dictionary - zadne alokace v hot pathu
    // (puvodni verze delala .Keys.ToArray() KAZDY fyzikalni krok = LINQ alokace).
    private readonly List<BodyHandle> _tracked = [];
    private readonly Dictionary<int, int> _trackedSlot = [];   // handle.Value -> slot v _prev
    private (Vector3 Pos, Quaternion Rot)[] _prev = new (Vector3, Quaternion)[256];

    public PhysicsWorld()
    {
        _dispatcher = new ThreadDispatcher(Math.Max(1, Environment.ProcessorCount - 1));
        Simulation = Simulation.Create(
            _pool,
            new NarrowPhaseCallbacks(),
            new PoseIntegratorCallbacks { Gravity = new Vector3(0, -9.81f, 0), LinearDamping = 0.03f, AngularDamping = 0.03f },
            new SolveDescription(velocityIterationCount: 8, substepCount: 1));
    }

    /// <summary>
    /// Fixni krok + akumulator. Bez tohohle mas fyziku zavislou na FPS
    /// a na 144 Hz monitoru se ti veci chovaji jinak nez na 60 Hz.
    /// </summary>
    public float Step(float frameTime)
    {
        _accumulator += frameTime;
        _accumulator = MathF.Min(_accumulator, 0.25f);   // pojistka proti "spiral of death"

        while (_accumulator >= FixedDt)
        {
            StorePreviousPoses();
            Simulation.Timestep(FixedDt, _dispatcher);
            _accumulator -= FixedDt;
        }

        return _accumulator / FixedDt;   // alpha pro interpolaci
    }

    private void StorePreviousPoses()
    {
        for (int i = 0; i < _tracked.Count; i++)
        {
            var body = Simulation.Bodies[_tracked[i]];
            _prev[i] = (body.Pose.Position, body.Pose.Orientation);
        }
    }

    public void Track(BodyHandle handle)
    {
        if (_tracked.Count == _prev.Length)
            Array.Resize(ref _prev, _prev.Length * 2);

        var body = Simulation.Bodies[handle];
        _trackedSlot[handle.Value] = _tracked.Count;
        _prev[_tracked.Count] = (body.Pose.Position, body.Pose.Orientation);
        _tracked.Add(handle);
    }

    /// <summary>Vysledna renderovaci poza: interpolace mezi predchozim a aktualnim fyzikalnim krokem.</summary>
    public (Vector3 Pos, Quaternion Rot) GetInterpolated(BodyHandle handle, float alpha)
    {
        var body = Simulation.Bodies[handle];
        if (!_trackedSlot.TryGetValue(handle.Value, out int slot))
            return (body.Pose.Position, body.Pose.Orientation);

        ref var prev = ref _prev[slot];
        return (
            Vector3.Lerp(prev.Pos, body.Pose.Position, alpha),
            Quaternion.Slerp(prev.Rot, body.Pose.Orientation, alpha));
    }

    public BodyHandle AddDynamicConvexHull(Vector3 position, Quaternion rotation, Span<Vector3> vertices, float mass, out Vector3 center)
    {
        BepuPhysics.Collidables.ConvexHullHelper.CreateShape(vertices, _pool, out center, out var hull);
        var inertia = hull.ComputeInertia(mass);
        var index = Simulation.Shapes.Add(hull);

        Vector3 offsetPos = position + Vector3.Transform(center, rotation);

        var handle = Simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(offsetPos, rotation), inertia,
            new CollidableDescription(index, 0.1f), new BodyActivityDescription(0.01f)));

        Track(handle);
        return handle;
    }

    public BodyHandle AddCharacter(Vector3 position, float radius, float length, float mass)
    {
        var capsule = new Capsule(radius, length);
        var index = Simulation.Shapes.Add(capsule);
        var inertia = capsule.ComputeInertia(mass);

        // Zamkneme rotaci (nekonecna setrvacnost na vsech osach)
        inertia.InverseInertiaTensor = default;

        var handle = Simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(position, Quaternion.Identity), inertia,
            new CollidableDescription(index, 0.1f), new BodyActivityDescription(0.01f)));

        Track(handle);
        return handle;
    }

    public BodyHandle AddDynamicBox(Vector3 position, Quaternion rotation, Vector3 size, float mass)
    {
        var box = new Box(size.X, size.Y, size.Z);
        var index = Simulation.Shapes.Add(box);
        var inertia = box.ComputeInertia(mass);

        var handle = Simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(position, rotation), inertia,
            new CollidableDescription(index, 0.1f), new BodyActivityDescription(0.01f)));

        Track(handle);
        return handle;
    }

    public StaticHandle AddStaticBox(Vector3 position, Vector3 size)
    {
        var index = Simulation.Shapes.Add(new Box(size.X, size.Y, size.Z));
        return Simulation.Statics.Add(new StaticDescription(position, index));
    }

    public void Dispose()
    {
        Simulation.Dispose();
        _dispatcher.Dispose();
        _pool.Clear();
    }
}
