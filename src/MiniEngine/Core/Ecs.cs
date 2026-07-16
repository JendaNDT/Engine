using System.Runtime.CompilerServices;

namespace MiniEngine.Core;

/// <summary>
/// Entita = index + generace. Generace brani "stale handle" bugu:
/// kdyz entitu smazes a index se recykluje, stary handle uz nesedi.
/// </summary>
public readonly record struct Entity(int Index, int Generation)
{
    public static readonly Entity Invalid = new(-1, 0);
    public bool IsValid => Index >= 0;
}

/// <summary>
/// Sparse-set uloziste jednoho typu komponenty.
/// - dense[]   : komponenty natesno za sebou (cache friendly iterace)
/// - entities[]: ke kazde dense komponente jeji entita
/// - sparse[]  : entity index -> dense index (nebo -1)
/// Iterace je linearni pruchod polem structu. Zadne alokace, zadne boxovani.
/// </summary>
public interface IStore
{
    void RemoveAt(int entityIndex);
}

public sealed class Store<T> : IStore where T : struct
{
    private T[] _dense = new T[64];
    private int[] _entities = new int[64];
    private int[] _sparse = [];
    private int _count;

    public int Count => _count;

    /// <summary>Span nad komponentami. Iteruj pres 'ref var x = ref span[i]', jinak kopirujes strukturu.</summary>
    public Span<T> Span => _dense.AsSpan(0, _count);
    public ReadOnlySpan<int> Entities => _entities.AsSpan(0, _count);

    public bool Has(int entityIndex) =>
        entityIndex < _sparse.Length && _sparse[entityIndex] >= 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get(int entityIndex) => ref _dense[_sparse[entityIndex]];

    public ref T Add(int entityIndex, in T value)
    {
        EnsureSparse(entityIndex);
        if (_sparse[entityIndex] >= 0)
        {
            ref var existing = ref _dense[_sparse[entityIndex]];
            existing = value;
            return ref existing;
        }

        if (_count == _dense.Length)
        {
            Array.Resize(ref _dense, _dense.Length * 2);
            Array.Resize(ref _entities, _entities.Length * 2);
        }

        _dense[_count] = value;
        _entities[_count] = entityIndex;
        _sparse[entityIndex] = _count;
        _count++;
        return ref _dense[_count - 1];
    }

    /// <summary>Swap-remove: posledni prvek se presune na uvolnene misto. O(1), ale meni poradi.</summary>
    public void RemoveAt(int entityIndex)
    {
        if (!Has(entityIndex)) return;

        int slot = _sparse[entityIndex];
        int last = _count - 1;

        _dense[slot] = _dense[last];
        _dense[last] = default; // Uvolneni reference pro GC
        _entities[slot] = _entities[last];
        _sparse[_entities[slot]] = slot;
        _sparse[entityIndex] = -1;
        _count--;
    }

    /// <summary>Odstrani vsechny komponenty (entity zustavaji zive).</summary>
    public void Clear()
    {
        _count = 0;
        Array.Fill(_sparse, -1);
    }

    private void EnsureSparse(int entityIndex)
    {
        if (entityIndex < _sparse.Length) return;

        int old = _sparse.Length;
        int size = Math.Max(64, Math.Max(entityIndex + 1, old * 2));
        Array.Resize(ref _sparse, size);
        for (int i = old; i < size; i++) _sparse[i] = -1;
    }
}

/// <summary>
/// Svet. Zamerne bez reflexe a bez generickych "query" builderu -
/// diky tomu je 100% NativeAOT-safe a cely se vejde do hlavy.
/// </summary>
public sealed class World
{
    private readonly List<int> _generations = [];
    private readonly List<int> _free = [];
    private readonly Dictionary<Type, IStore> _stores = [];

    public Entity Create()
    {
        if (_free.Count > 0)
        {
            int idx = _free[_free.Count - 1];
            _free.RemoveAt(_free.Count - 1);
            return new Entity(idx, _generations[idx]);
        }

        _generations.Add(1);
        return new Entity(_generations.Count - 1, 1);
    }

    public void Destroy(Entity e)
    {
        if (!IsAlive(e)) return;
        foreach (var store in _stores.Values)
            ((IStore)store).RemoveAt(e.Index);

        _generations[e.Index]++;
        _free.Add(e.Index);
    }

    /// <summary>Vrátí entitu zpět do života (pouze pro Undo/Redo v editoru).</summary>
    public void Undestroy(Entity e)
    {
        if (!e.IsValid || e.Index >= _generations.Count) return;
        _generations[e.Index] = e.Generation;
        _free.Remove(e.Index);
    }

    public bool IsAlive(Entity e) =>
        e.IsValid && e.Index < _generations.Count && _generations[e.Index] == e.Generation;

    /// <summary>Handle na entitu podle indexu s aktualni generaci (pro editor - vyber, mazani).</summary>
    public Entity EntityFromIndex(int index) =>
        index >= 0 && index < _generations.Count ? new Entity(index, _generations[index]) : Entity.Invalid;

    /// <summary>Store si vytahni JEDNOU pri startu do pole, ne per frame (dictionary lookup).</summary>
    public Store<T> Store<T>() where T : struct
    {
        if (_stores.TryGetValue(typeof(T), out var s)) return (Store<T>)s;
        var store = new Store<T>();
        _stores[typeof(T)] = store;
        return store;
    }

    public ref T Add<T>(Entity e, in T value) where T : struct => ref Store<T>().Add(e.Index, value);
    public ref T Get<T>(Entity e) where T : struct => ref Store<T>().Get(e.Index);
    public bool Has<T>(Entity e) where T : struct => Store<T>().Has(e.Index);
}
