using System;
using System.Numerics;
using Raylib_cs;
using MiniEngine.Core;
using Transform = MiniEngine.Core.Transform;
using MiniEngine.Assets;

namespace MiniEngine.Rendering;

public struct Particle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Color;
    public float Size;
    public float Age;
    public float Lifetime;
    public bool Active;

    // Pocatecni a koncove stavy pro interpolaci bez nutnosti drzet emitor
    public Vector3 ColorStart;
    public Vector3 ColorEnd;
    public float SizeStart;
    public float SizeMiddle;
    public float SizeEnd;
    public Vector3 Gravity;
    public string TexturePath;
}

public sealed class ParticleSystem
{
    private readonly Particle[] _particles = new Particle[2000];
    private int _nextFreeIndex;
    private readonly Random _rng = new(1337);

    public int GetActiveParticlesCount()
    {
        int count = 0;
        for (int i = 0; i < _particles.Length; i++)
        {
            if (_particles[i].Active) count++;
        }
        return count;
    }

    public void Update(float dt, Store<Transform> transforms, Store<ParticleEmitter> emitters)
    {
        // 1. Spawnovani novych castic
        var emitterSpan = emitters.Span;
        var entities = emitters.Entities;

        for (int i = 0; i < emitterSpan.Length; i++)
        {
            ref var emitter = ref emitterSpan[i];
            if (!emitter.Active) continue;

            ref var t = ref transforms.Get(entities[i]);
            Vector3 spawnPos = t.World.Translation;

            emitter.SpawnTimer += dt;
            float spawnInterval = 1f / emitter.SpawnRate;
            while (emitter.SpawnTimer >= spawnInterval)
            {
                emitter.SpawnTimer -= spawnInterval;
                Spawn(spawnPos, ref emitter);
            }
        }

        // 2. Aktualizace stavajicich castic
        for (int i = 0; i < _particles.Length; i++)
        {
            if (!_particles[i].Active) continue;

            ref var p = ref _particles[i];
            p.Age += dt;
            if (p.Age >= p.Lifetime)
            {
                p.Active = false;
                continue;
            }

            p.Velocity += p.Gravity * dt;
            p.Position += p.Velocity * dt;

            float progress = p.Age / p.Lifetime;
            p.Color = Vector3.Lerp(p.ColorStart, p.ColorEnd, progress);

            // Interpolace velikosti Start -> Middle -> End
            if (progress < 0.5f)
            {
                float tSegment = progress * 2f;
                p.Size = p.SizeStart + tSegment * (p.SizeMiddle - p.SizeStart);
            }
            else
            {
                float tSegment = (progress - 0.5f) * 2f;
                p.Size = p.SizeMiddle + tSegment * (p.SizeEnd - p.SizeMiddle);
            }
        }
    }

    public void Draw(Camera3D camera, AssetManager assets)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            if (!_particles[i].Active) continue;

            ref var p = ref _particles[i];
            var color = new Color(
                (byte)Math.Clamp(p.Color.X * 255f, 0f, 255f),
                (byte)Math.Clamp(p.Color.Y * 255f, 0f, 255f),
                (byte)Math.Clamp(p.Color.Z * 255f, 0f, 255f),
                (byte)255
            );

            // Pokud je nastavena textura, kreslime ji jako billboard otoceny ke kamere
            if (!string.IsNullOrEmpty(p.TexturePath))
            {
                var tex = assets.GetTexture(p.TexturePath);
                if (tex.Id > 0)
                {
                    Raylib.DrawBillboard(camera, tex, p.Position, p.Size, color);
                    continue;
                }
            }

            Raylib.DrawCubeV(p.Position, new Vector3(p.Size), color);
        }
    }

    private void Spawn(Vector3 position, ref ParticleEmitter emitter)
    {
        int startIndex = _nextFreeIndex;
        do
        {
            if (!_particles[_nextFreeIndex].Active)
            {
                ref var p = ref _particles[_nextFreeIndex];
                p.Active = true;
                p.Position = position;

                float rx = (float)_rng.NextDouble();
                float ry = (float)_rng.NextDouble();
                float rz = (float)_rng.NextDouble();

                p.Velocity = new Vector3(
                    emitter.VelocityMin.X + rx * (emitter.VelocityMax.X - emitter.VelocityMin.X),
                    emitter.VelocityMin.Y + ry * (emitter.VelocityMax.Y - emitter.VelocityMin.Y),
                    emitter.VelocityMin.Z + rz * (emitter.VelocityMax.Z - emitter.VelocityMin.Z)
                );

                p.Age = 0f;
                p.Lifetime = emitter.Lifetime * (float)(0.85 + _rng.NextDouble() * 0.3); // +/- 15% variace

                p.ColorStart = emitter.ColorStart;
                p.ColorEnd = emitter.ColorEnd;
                p.SizeStart = emitter.SizeStart;
                p.SizeMiddle = emitter.SizeMiddle;
                p.SizeEnd = emitter.SizeEnd;
                p.Gravity = emitter.Gravity;
                p.TexturePath = emitter.TexturePath;

                p.Color = p.ColorStart;
                p.Size = p.SizeStart;

                _nextFreeIndex = (_nextFreeIndex + 1) % _particles.Length;
                return;
            }
            _nextFreeIndex = (_nextFreeIndex + 1) % _particles.Length;
        } while (_nextFreeIndex != startIndex);
    }
}
