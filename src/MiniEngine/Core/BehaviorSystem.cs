using System;
using System.Numerics;
using MiniEngine.Core;
using MiniEngine.Physics;
using BepuPhysics;

namespace MiniEngine.Core;

public sealed class BehaviorSystem
{
    private float _time;

    public void Reset()
    {
        _time = 0f;
    }

    public void Update(float dt, Store<Transform> transforms, Store<BehaviorComponent> behaviors, Store<RigidBodyRef> bodies, PhysicsWorld? physics)
    {
        if (physics == null)
        {
            var behaviorsSpan = behaviors.Span;
            for (int i = 0; i < behaviorsSpan.Length; i++)
            {
                behaviorsSpan[i].Initialized = false;
            }
            _time = 0f;
            return;
        }

        _time += dt;

        var span = behaviors.Span;
        var entities = behaviors.Entities;

        for (int i = 0; i < span.Length; i++)
        {
            ref var b = ref span[i];
            if (!b.Active) continue;

            int ent = entities[i];
            if (!transforms.Has(ent)) continue;

            ref var t = ref transforms.Get(ent);

            if (!b.Initialized)
            {
                b.StartPosition = t.Position;
                b.StartRotation = t.Rotation;
                b.Initialized = true;
            }

            Vector3 targetPos = b.StartPosition;
            Quaternion targetRot = b.StartRotation;

            if (b.Type == 0) // Rotator
            {
                float angle = _time * b.Speed;
                targetRot = b.StartRotation * Quaternion.CreateFromAxisAngle(b.Axis, angle);
            }
            else if (b.Type == 1) // Ping-Pong
            {
                float offset = MathF.Sin(_time * b.Speed) * b.Range;
                targetPos = b.StartPosition + b.Axis * offset;
            }
            else if (b.Type == 2) // Orbiter
            {
                float angle = _time * b.Speed;
                float x = MathF.Cos(angle) * b.Range;
                float z = MathF.Sin(angle) * b.Range;
                targetPos = b.StartPosition + new Vector3(x, 0f, z);
            }
            else if (b.Type == 3) // Bobber
            {
                float offset = MathF.Sin(_time * b.Frequency * 2f * MathF.PI) * b.Range;
                targetPos = b.StartPosition + new Vector3(0f, offset, 0f);
                float rotAngle = MathF.Cos(_time * b.Frequency * MathF.PI) * 0.1f;
                targetRot = b.StartRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, rotAngle);
            }

            if (bodies.Has(ent))
            {
                ref var bodyRef = ref bodies.Get(ent);
                var handle = new BodyHandle(bodyRef.BodyHandle);
                var bepuBody = physics.Simulation.Bodies[handle];

                if (b.Type == 0) // Rotator
                {
                    bepuBody.Velocity.Angular = b.Axis * b.Speed;
                }
                else // Pohybove typy
                {
                    Vector3 diff = targetPos - bepuBody.Pose.Position;
                    // Rychlost = dráha / čas (dt)
                    bepuBody.Velocity.Linear = diff / dt;
                }
            }
            else
            {
                t.Position = targetPos;
                t.Rotation = targetRot;
            }
        }
    }

    public void RestoreInitialStates(Store<Transform> transforms, Store<BehaviorComponent> behaviors)
    {
        var span = behaviors.Span;
        var entities = behaviors.Entities;

        for (int i = 0; i < span.Length; i++)
        {
            ref var b = ref span[i];
            if (b.Initialized && transforms.Has(entities[i]))
            {
                ref var t = ref transforms.Get(entities[i]);
                t.Position = b.StartPosition;
                t.Rotation = b.StartRotation;
                b.Initialized = false;
            }
        }
    }
}
