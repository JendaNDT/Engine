using System;
using System.Collections.Generic;
using MiniEngine.Core;
using MiniEngine.Rendering;
using MiniEngine.Engine;

namespace MiniEngine.Editor.History;

/// <summary>
/// Kompozitní příkaz seskupující více sub-příkazů do jedné atomické transakce.
/// </summary>
public sealed class CompositeCommand : IEditorCommand
{
    private readonly List<IEditorCommand> _commands = new();

    public bool IsEmpty => _commands.Count == 0;

    public void Add(IEditorCommand command)
    {
        if (command != null)
            _commands.Add(command);
    }

    public void Execute()
    {
        for (int i = 0; i < _commands.Count; i++)
        {
            _commands[i].Execute();
        }
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}

/// <summary>
/// Příkaz vykonávající libovolný delegát pro Execute a Undo (pro side-effecty).
/// </summary>
public sealed class DelegateCommand : IEditorCommand
{
    private readonly Action _execute;
    private readonly Action _undo;

    public DelegateCommand(Action execute, Action undo)
    {
        _execute = execute;
        _undo = undo;
    }

    public void Execute() => _execute?.Invoke();
    public void Undo() => _undo?.Invoke();
}

/// <summary>
/// Generický příkaz pro změnu, přidání nebo odebrání komponenty typu T v ECS.
/// </summary>
public sealed class ComponentChangeCommand<T> : IEditorCommand where T : struct
{
    private readonly Store<T> _store;
    private readonly int _entityIndex;
    private readonly T? _oldValue;
    private readonly T? _newValue;

    public ComponentChangeCommand(Store<T> store, int entityIndex, T? oldValue, T? newValue)
    {
        _store = store;
        _entityIndex = entityIndex;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public void Execute()
    {
        Apply(_newValue);
    }

    public void Undo()
    {
        Apply(_oldValue);
    }

    private void Apply(T? value)
    {
        if (value.HasValue)
        {
            _store.Add(_entityIndex, value.Value);
        }
        else
        {
            _store.RemoveAt(_entityIndex);
        }
    }
}

/// <summary>
/// Základní příkaz pro zničení/obnovení entity na úrovni ECS registru generací.
/// </summary>
public sealed class DestroyBaseCommand : IEditorCommand
{
    private readonly World _world;
    private readonly Entity _entity;

    public DestroyBaseCommand(World world, Entity entity)
    {
        _world = world;
        _entity = entity;
    }

    public void Execute()
    {
        _world.Destroy(_entity);
    }

    public void Undo()
    {
        _world.Undestroy(_entity);
    }
}

/// <summary>
/// Základní příkaz pro vytvoření/odebrání entity na úrovni ECS registru generací.
/// </summary>
public sealed class CreateBaseCommand : IEditorCommand
{
    private readonly World _world;
    private readonly Entity _entity;

    public CreateBaseCommand(World world, Entity entity)
    {
        _world = world;
        _entity = entity;
    }

    public void Execute()
    {
        _world.Undestroy(_entity);
    }

    public void Undo()
    {
        _world.Destroy(_entity);
    }
}

/// <summary>
/// Pomocná třída pro snímkování stavu všech komponent jedné entity.
/// Umožňuje bezalokační porovnání změn v editoru a generování delta příkazů.
/// </summary>
public sealed class EntitySnapshot
{
    public Name? Name;
    public Transform? Transform;
    public MeshRenderer? Renderer;
    public LightComponent? Light;
    public BehaviorComponent? Behavior;
    public ParticleEmitter? Emitter;
    public AudioSourceComponent? Audio;
    public TriggerComponent? Trigger;
    public ActionComponent? Action;

    public static EntitySnapshot Capture(World world, int index)
    {
        var snap = new EntitySnapshot();
        if (index < 0) return snap;

        var entity = world.EntityFromIndex(index);
        if (!world.IsAlive(entity)) return snap;

        if (world.Has<Name>(entity)) snap.Name = world.Get<Name>(entity);
        if (world.Has<Transform>(entity)) snap.Transform = world.Get<Transform>(entity);
        if (world.Has<MeshRenderer>(entity)) snap.Renderer = world.Get<MeshRenderer>(entity);
        if (world.Has<LightComponent>(entity)) snap.Light = world.Get<LightComponent>(entity);
        if (world.Has<BehaviorComponent>(entity)) snap.Behavior = world.Get<BehaviorComponent>(entity);
        if (world.Has<ParticleEmitter>(entity)) snap.Emitter = world.Get<ParticleEmitter>(entity);
        if (world.Has<AudioSourceComponent>(entity)) snap.Audio = world.Get<AudioSourceComponent>(entity);
        if (world.Has<TriggerComponent>(entity)) snap.Trigger = world.Get<TriggerComponent>(entity);
        if (world.Has<ActionComponent>(entity)) snap.Action = world.Get<ActionComponent>(entity);

        return snap;
    }

    public static IEditorCommand CreateRestoreCommand(World world, int index, EntitySnapshot oldSnap, EntitySnapshot newSnap)
    {
        var composite = new CompositeCommand();
        if (index < 0) return composite;

        CompareAndAdd(composite, world.Store<Name>(), index, oldSnap?.Name, newSnap?.Name);
        CompareAndAdd(composite, world.Store<Transform>(), index, oldSnap?.Transform, newSnap?.Transform);
        CompareAndAdd(composite, world.Store<MeshRenderer>(), index, oldSnap?.Renderer, newSnap?.Renderer);
        CompareAndAdd(composite, world.Store<LightComponent>(), index, oldSnap?.Light, newSnap?.Light);
        CompareAndAdd(composite, world.Store<BehaviorComponent>(), index, oldSnap?.Behavior, newSnap?.Behavior);
        CompareAndAdd(composite, world.Store<ParticleEmitter>(), index, oldSnap?.Emitter, newSnap?.Emitter);
        CompareAndAdd(composite, world.Store<AudioSourceComponent>(), index, oldSnap?.Audio, newSnap?.Audio);
        CompareAndAdd(composite, world.Store<TriggerComponent>(), index, oldSnap?.Trigger, newSnap?.Trigger);
        CompareAndAdd(composite, world.Store<ActionComponent>(), index, oldSnap?.Action, newSnap?.Action);

        return composite;
    }

    private static void CompareAndAdd<T>(CompositeCommand composite, Store<T> store, int index, T? oldVal, T? newVal) where T : struct
    {
        if (!Nullable.Equals(oldVal, newVal))
        {
            composite.Add(new ComponentChangeCommand<T>(store, index, oldVal, newVal));
        }
    }
}

/// <summary>
/// Příkaz pro změnu a obnovení globálního osvětlení a nastavení post-processingu v editoru.
/// </summary>
public sealed class GlobalSettingsCommand : IEditorCommand
{
    private readonly LightingShader _lighting;
    private readonly PostProcessing _postProcessor;
    private readonly LightingSettingsData _oldLighting;
    private readonly LightingSettingsData _newLighting;
    private readonly PostProcessingSettingsData _oldPostProcess;
    private readonly PostProcessingSettingsData _newPostProcess;

    public GlobalSettingsCommand(
        LightingShader lighting, PostProcessing postProcessor,
        LightingSettingsData oldLighting, LightingSettingsData newLighting,
        PostProcessingSettingsData oldPostProcess, PostProcessingSettingsData newPostProcess)
    {
        _lighting = lighting;
        _postProcessor = postProcessor;
        _oldLighting = oldLighting;
        _newLighting = newLighting;
        _oldPostProcess = oldPostProcess;
        _newPostProcess = newPostProcess;
    }

    public void Execute() => Apply(_newLighting, _newPostProcess);
    public void Undo() => Apply(_oldLighting, _oldPostProcess);

    private void Apply(LightingSettingsData lighting, PostProcessingSettingsData pp)
    {
        if (lighting == null || pp == null) return;

        // Použití nastavení osvětlení
        _lighting.SunDirection = new System.Numerics.Vector3(lighting.SunDirection[0], lighting.SunDirection[1], lighting.SunDirection[2]);
        _lighting.SunColor = new System.Numerics.Vector3(lighting.SunColor[0], lighting.SunColor[1], lighting.SunColor[2]);
        _lighting.SunIntensity = lighting.SunIntensity;
        _lighting.Ambient = new System.Numerics.Vector3(lighting.Ambient[0], lighting.Ambient[1], lighting.Ambient[2]);
        _lighting.SpecStrength = lighting.SpecStrength;

        // Použití post-processingu
        _postProcessor.Enabled = pp.Enabled;
        _postProcessor.BloomIntensity = pp.BloomIntensity;
        _postProcessor.BloomThreshold = pp.BloomThreshold;
        _postProcessor.VignettePower = pp.VignettePower;
        _postProcessor.Saturation = pp.Saturation;
        _postProcessor.Contrast = pp.Contrast;
        _postProcessor.ChromaticAberration = pp.ChromaticAberration;
    }
}
