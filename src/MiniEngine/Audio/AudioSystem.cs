using System;
using System.Numerics;
using Raylib_cs;
using MiniEngine.Core;
using MiniEngine.Assets;
using Transform = MiniEngine.Core.Transform;

namespace MiniEngine.Audio;

public sealed class AudioSystem
{
    public void Update(Camera3D camera, Store<Transform> transforms, Store<AudioSourceComponent> audioSources, AssetManager assets)
    {
        var sources = audioSources.Span;
        var entities = audioSources.Entities;

        Vector3 listenerPos = camera.Position;
        Vector3 cameraDir = camera.Target - camera.Position;
        Vector3 listenerForward = cameraDir.LengthSquared() > 0.001f ? Vector3.Normalize(cameraDir) : new Vector3(0, 0, -1);
        Vector3 rightDir = Vector3.Cross(listenerForward, camera.Up);
        Vector3 listenerRight = rightDir.LengthSquared() > 0.001f ? Vector3.Normalize(rightDir) : new Vector3(1, 0, 0);

        for (int i = 0; i < sources.Length; i++)
        {
            ref var source = ref sources[i];
            int ent = entities[i];

            if (string.IsNullOrEmpty(source.ClipPath))
            {
                source.IsPlaying = false;
                continue;
            }

            Sound sound = assets.GetSound(source.ClipPath);
            if (sound.FrameCount == 0) continue;

            if (source.Active)
            {
                // Pokud ještě nehraje, spustíme ho
                if (!source.IsPlaying)
                {
                    Raylib.PlaySound(sound);
                    source.IsPlaying = true;
                }

                // Pokud dohrál a má se smyčkovat, pustíme znovu
                if (!Raylib.IsSoundPlaying(sound) && source.Loop)
                {
                    Raylib.PlaySound(sound);
                }

                // Spočítáme 3D spatializaci
                float finalVolume = source.Volume;
                float pan = 0.5f;

                if (source.Spatial3D && transforms.Has(ent))
                {
                    ref var t = ref transforms.Get(ent);
                    Vector3 sourcePos = t.World.Translation;
                    
                    float dist = Vector3.Distance(sourcePos, listenerPos);
                    
                    // Útlum podle vzdálenosti (roll-off)
                    float minDistance = 2f;
                    float maxDistance = 35f;
                    float volumeFactor = 1f;
                    if (dist > minDistance)
                    {
                        volumeFactor = MathF.Max(0f, 1f - (dist - minDistance) / (maxDistance - minDistance));
                    }
                    finalVolume = source.Volume * volumeFactor;

                    // Panování do uší
                    Vector3 dirToSource = sourcePos - listenerPos;
                    if (dirToSource.LengthSquared() > 0.001f)
                    {
                        dirToSource = Vector3.Normalize(dirToSource);
                        float dot = Vector3.Dot(dirToSource, listenerRight);
                        pan = Math.Clamp(dot * 0.5f + 0.5f, 0f, 1f);
                    }
                }

                Raylib.SetSoundVolume(sound, finalVolume);
                Raylib.SetSoundPitch(sound, source.Pitch);
                Raylib.SetSoundPan(sound, pan);
            }
            else
            {
                // Zastavit pokud byl deaktivován
                if (source.IsPlaying)
                {
                    Raylib.StopSound(sound);
                    source.IsPlaying = false;
                }
            }
        }
    }

    public void StopAll(Store<AudioSourceComponent> audioSources, AssetManager assets)
    {
        var sources = audioSources.Span;
        for (int i = 0; i < sources.Length; i++)
        {
            ref var source = ref sources[i];
            if (source.IsPlaying && !string.IsNullOrEmpty(source.ClipPath))
            {
                Sound sound = assets.GetSound(source.ClipPath);
                if (sound.FrameCount > 0)
                {
                    Raylib.StopSound(sound);
                }
                source.IsPlaying = false;
            }
        }
    }
}
