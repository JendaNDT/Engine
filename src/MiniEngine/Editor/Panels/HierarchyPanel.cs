using System.Numerics;
using ImGuiNET;
using MiniEngine.Core;
using Transform = MiniEngine.Core.Transform;

namespace MiniEngine.Editor;

/// <summary>
/// Strom entit ve scene + tlacitka pro pridani/duplikaci/smazani.
/// Deti jsou zanorene pod rodicem, prevesuje se pretazenim (drag &amp; drop):
/// entita na entitu = novy rodic, entita na zonu dole = bez rodice.
/// Panel se kresli jen v editoru - alokace (strom, stringy) tady nevadi,
/// merena zona 0 B/frame je jen Update + RenderScene.
/// </summary>
public sealed class HierarchyPanel
{
    private readonly World _world;
    private readonly Store<Transform> _transforms;
    private readonly Store<MeshRenderer> _renderers;
    private readonly Store<Name> _names;
    private readonly Random _rng = new();

    private string _searchQuery = "";
    private bool _forceExpandAll;
    private bool _forceCollapseAll;

    // Strom se stavi kazdy frame znovu (poradi/rodice se mohli zmenit).
    private readonly Dictionary<int, List<int>> _children = [];
    private readonly List<int> _roots = [];

    // Drag & drop: payload ImGui nese jen "tahne se entita", index si drzime sami.
    private int _draggedEntity = -1;
    private (int Child, int NewParent) _pendingReparent = (-2, -2);
    private int _clicked = -2;

    /// <summary>Callbacky na ulozeni/nacteni sceny - nastavuje Game (jednou, ne per frame).</summary>
    public Action? OnSave;
    public Action? OnLoad;

    /// <summary>Duplikace/smazani vybrane entity - logika zije v Game (resi i refcount modelu).</summary>
    public Action? OnDuplicate;
    public Action? OnDelete;

    /// <summary>Prevaseni entity (child, newParent; -1 = koren) - logika v Game.</summary>
    public Action<int, int>? OnReparent;
    public Action? OnChanged;
    public Action? OnCreateEmpty;
    public Action<int>? OnCreateChildOf;
    public Action? OnSavePrefab;

    private int _renamingEntity = -1;
    private string _renameBuffer = "";
    private bool _justStartedRename = false;
    private int _pendingCreateChildOf = -2;

    /// <summary>Zprava o poslednim ulozeni/nacteni (nebo chybe) - nastavuje Game.</summary>
    public string SceneStatus = "";

    public HierarchyPanel(World world)
    {
        _world = world;
        _transforms = world.Store<Transform>();
        _renderers = world.Store<MeshRenderer>();
        _names = world.Store<Name>();
    }

    public void Draw(EditorSelection selection)
    {
        ImGui.Begin("Hierarchy");

        if (ImGui.Button("+ Krychle"))
        {
            var e = CreateCube();
            selection.EntityIndex = e.Index;
            OnChanged?.Invoke();
        }
        ImGui.SameLine();
        if (ImGui.Button("+ Prázdný"))
        {
            OnCreateEmpty?.Invoke();
        }

        if (selection.HasSelection && _transforms.Has(selection.EntityIndex))
        {
            ImGui.SameLine();
            if (ImGui.Button("Duplikovat")) OnDuplicate?.Invoke();

            ImGui.SameLine();
            if (ImGui.Button("Smazat")) OnDelete?.Invoke();

            ImGui.SameLine();
            if (ImGui.Button("Uložit Prefab")) OnSavePrefab?.Invoke();
        }

        if (ImGui.Button("Ulozit scenu")) OnSave?.Invoke();
        ImGui.SameLine();
        if (ImGui.Button("Nacist scenu")) OnLoad?.Invoke();

        if (SceneStatus.Length > 0)
            ImGui.TextDisabled(SceneStatus);

        ImGui.Separator();

        // Vyhledávání a hromadné rozbalení/sbalení
        ImGui.InputText("Hledat", ref _searchQuery, 64);
        if (ImGui.Button("Rozbalit vše")) _forceExpandAll = true;
        ImGui.SameLine();
        if (ImGui.Button("Sbalit vše")) _forceCollapseAll = true;

        ImGui.Separator();

        BuildTree();

        // Zmeny (vyber, prevaseni) se aplikuji az PO pruchodu stromem -
        // nemenit strukturu, pres kterou prave iterujes.
        _clicked = -2;
        _pendingReparent = (-2, -2);

        foreach (int root in _roots)
            DrawNode(root, selection);

        // Drop zona pro zruseni rodice.
        ImGui.Separator();
        ImGui.Selectable("(pretazenim sem zrusis rodice)");
        if (ImGui.BeginDragDropTarget())
        {
            AcceptEntityDrop(-1);
            ImGui.EndDragDropTarget();
        }

        if (_clicked != -2)
            selection.EntityIndex = _clicked;

        if (_pendingReparent.Child != -2)
            OnReparent?.Invoke(_pendingReparent.Child, _pendingReparent.NewParent);

        if (_pendingCreateChildOf != -2)
        {
            OnCreateChildOf?.Invoke(_pendingCreateChildOf);
            _pendingCreateChildOf = -2;
        }

        _forceExpandAll = false;
        _forceCollapseAll = false;

        ImGui.End();
    }

    private void BuildTree()
    {
        foreach (var list in _children.Values) list.Clear();
        _roots.Clear();

        var entities = _transforms.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            int idx = entities[i];
            int parent = _transforms.Get(idx).Parent;

            if (parent >= 0 && _transforms.Has(parent))
            {
                if (!_children.TryGetValue(parent, out var list))
                    _children[parent] = list = [];
                list.Add(idx);
            }
            else
            {
                _roots.Add(idx);
            }
        }
    }

    private void DrawNode(int idx, EditorSelection selection)
    {
        if (!NodeOrDescendantsMatch(idx, _searchQuery)) return;

        bool hasKids = _children.TryGetValue(idx, out var kids) && kids.Count > 0;

        var flags = ImGuiTreeNodeFlags.OpenOnArrow |
                    ImGuiTreeNodeFlags.OpenOnDoubleClick |
                    ImGuiTreeNodeFlags.SpanAvailWidth |
                    ImGuiTreeNodeFlags.DefaultOpen;
        if (!hasKids) flags |= ImGuiTreeNodeFlags.Leaf;
        if (idx == selection.EntityIndex) flags |= ImGuiTreeNodeFlags.Selected;

        if (_forceExpandAll)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        }
        else if (_forceCollapseAll)
        {
            ImGui.SetNextItemOpen(false, ImGuiCond.Always);
        }

        string name = _names.Has(idx) ? _names.Get(idx).Value : $"Entity {idx}";
        bool open = false;

        if (idx == _renamingEntity)
        {
            ImGui.PushItemWidth(-1f);
            string buf = _renameBuffer;
            if (ImGui.InputText($"##Rename{idx}", ref buf, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (_names.Has(idx))
                {
                    ref var n = ref _names.Get(idx);
                    n.Value = buf;
                }
                _renamingEntity = -1;
                OnChanged?.Invoke();
            }
            else
            {
                _renameBuffer = buf;
            }

            if (!ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsItemHovered())
            {
                if (_names.Has(idx))
                {
                    ref var n = ref _names.Get(idx);
                    n.Value = _renameBuffer;
                }
                _renamingEntity = -1;
                OnChanged?.Invoke();
            }

            if (_justStartedRename)
            {
                ImGui.SetKeyboardFocusHere(-1);
                _justStartedRename = false;
            }
            ImGui.PopItemWidth();
        }
        else
        {
            bool isMatched = !string.IsNullOrEmpty(_searchQuery) && name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase);
            if (isMatched)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.70f, 0.25f, 1.00f));
            }

            open = ImGui.TreeNodeEx($"{name}##{idx}", flags);

            if (isMatched)
            {
                ImGui.PopStyleColor();
            }

            // Klik na sipku rozbaluje, klik na jmeno vybira.
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
                _clicked = idx;

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _renamingEntity = idx;
                _renameBuffer = name;
                _justStartedRename = true;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _clicked = idx;
                ImGui.OpenPopup($"EntityContextMenu##{idx}");
            }
        }

        if (ImGui.BeginDragDropSource())
        {
            _draggedEntity = idx;
            ImGui.SetDragDropPayload("MINIENGINE_ENTITY", IntPtr.Zero, 0);
            ImGui.Text(name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            AcceptEntityDrop(idx);
            ImGui.EndDragDropTarget();
        }

        // Popup context menu pro entitu
        if (ImGui.BeginPopup($"EntityContextMenu##{idx}"))
        {
            if (ImGui.MenuItem("Přejmenovat (Double Click)"))
            {
                _renamingEntity = idx;
                _renameBuffer = name;
                _justStartedRename = true;
            }
            if (ImGui.MenuItem("Duplikovat"))
            {
                OnDuplicate?.Invoke();
            }
            if (ImGui.MenuItem("Smazat"))
            {
                OnDelete?.Invoke();
            }
            if (ImGui.MenuItem("Vynulovat Transform"))
            {
                if (_transforms.Has(idx))
                {
                    ref var t = ref _transforms.Get(idx);
                    t.Position = Vector3.Zero;
                    t.Rotation = Quaternion.Identity;
                    t.Scale = Vector3.One;
                    OnChanged?.Invoke();
                }
            }
            if (ImGui.MenuItem("Vytvořit prázdného potomka"))
            {
                _pendingCreateChildOf = idx;
            }
            ImGui.EndPopup();
        }

        if (open)
        {
            if (hasKids)
                foreach (int child in kids!)
                    DrawNode(child, selection);

            ImGui.TreePop();
        }
    }

    private unsafe void AcceptEntityDrop(int newParent)
    {
        var payload = ImGui.AcceptDragDropPayload("MINIENGINE_ENTITY");
        if (payload.NativePtr == null) return;   // nic se sem zrovna nepousti

        if (_draggedEntity >= 0 && _draggedEntity != newParent)
            _pendingReparent = (_draggedEntity, newParent);

        _draggedEntity = -1;
    }

    private Entity CreateCube()
    {
        var e = _world.Create();

        var t = Transform.Identity;
        t.Position = new Vector3(
            (float)(_rng.NextDouble() * 10 - 5),
            (float)(_rng.NextDouble() * 3),
            (float)(_rng.NextDouble() * 10 - 5));
        _world.Add(e, t);

        _world.Add(e, new MeshRenderer
        {
            ModelHandle = -1,
            Tint = new Vector3(0.8f, 0.8f, 0.8f),
            Visible = true
        });

        _world.Add(e, new Name { Value = $"Krychle {e.Index}" });
        return e;
    }

    private bool NodeOrDescendantsMatch(int idx, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        
        string name = _names.Has(idx) ? _names.Get(idx).Value : $"Entity {idx}";
        if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;

        if (_children.TryGetValue(idx, out var kids))
        {
            foreach (int kid in kids)
            {
                if (NodeOrDescendantsMatch(kid, query)) return true;
            }
        }
        return false;
    }
}
