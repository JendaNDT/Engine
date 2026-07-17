using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using MiniEngine.Core;

namespace MiniEngine.Editor
{
    public sealed class NodeEditorPanel
    {
        private readonly Store<BehaviorGraphComponent> _graphStore;
        private readonly Store<Name> _names;
        private readonly Store<Transform> _transforms;

        // Stav táhnutí spojení
        private int _draggedPinId = -1;
        private string _draggedPinKind = ""; // Input, Output
        private string _draggedPinType = ""; // Trigger, Float, etc.
        private Vector2 _draggedPinPos = Vector2.Zero;

        // Stav táhnutí uzlu
        private int _draggedNodeId = -1;
        private Vector2 _draggedNodeOffset = Vector2.Zero;

        // ID generátory
        private int _nextId = 1000;

        // Vybraná entita v minulém snímku (pro detekci změny výběru)
        private int _lastSelection = -1;
        private BehaviorGraph? _currentGraph;

        public NodeEditorPanel(World world)
        {
            _graphStore = world.Store<BehaviorGraphComponent>();
            _names = world.Store<Name>();
            _transforms = world.Store<Transform>();
        }

        public void Draw(EditorSelection selection)
        {
            ImGui.Begin("Behavior Node Graph");
            DrawInner(selection);
            ImGui.End();
        }

        public void DrawInner(EditorSelection selection)
        {
            int e = selection.EntityIndex;
            if (e < 0 || !_transforms.Has(e))
            {
                ImGui.TextDisabled("Vyber entitu s komponentou transformu.");
                return;
            }

            if (e != _lastSelection)
            {
                _currentGraph = null;
                _lastSelection = e;
            }

            if (!_graphStore.Has(e))
            {
                if (ImGui.Button("Přidat Behavior Graph komponentu"))
                {
                    _graphStore.Add(e, new BehaviorGraphComponent { Enabled = true, GraphJson = "" });
                }
                ImGui.End();
                return;
            }

            ref var comp = ref _graphStore.Get(e);
            
            // Načtení nebo deserializace grafu
            if (_currentGraph == null)
            {
                if (string.IsNullOrEmpty(comp.GraphJson))
                {
                    _currentGraph = new BehaviorGraph();
                    // Přidáme výchozí Event uzel
                    AddNodeInternal(_currentGraph, "Event_OnStart", 50, 50);
                }
                else
                {
                    try
                    {
                        _currentGraph = System.Text.Json.JsonSerializer.Deserialize(comp.GraphJson, MiniEngine.Engine.SceneJsonContext.Default.BehaviorGraph);
                    }
                    catch
                    {
                        _currentGraph = new BehaviorGraph();
                    }
                }
                RebuildIds();
            }

            ImGui.Checkbox("Aktivní", ref comp.Enabled);
            ImGui.SameLine();
            if (ImGui.Button("Resetovat graf"))
            {
                _currentGraph = new BehaviorGraph();
                AddNodeInternal(_currentGraph, "Event_OnStart", 50, 50);
                RebuildIds();
                SaveGraphToComponent(ref comp);
            }

            ImGui.Separator();

            // Canvas pro kreslení uzlů
            Vector2 canvasPos = ImGui.GetCursorScreenPos();
            Vector2 canvasSize = ImGui.GetContentRegionAvail();
            if (canvasSize.X < 100f) canvasSize.X = 100f;
            if (canvasSize.Y < 100f) canvasSize.Y = 100f;

            // Rámeček plátna
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRect(canvasPos, canvasPos + canvasSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)));
            drawList.PushClipRect(canvasPos, canvasPos + canvasSize, true);

            // Mřížka na pozadí
            float gridScale = 32.0f;
            uint gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.23f, 0.4f));
            for (float x = 0; x < canvasSize.X; x += gridScale)
            {
                drawList.AddLine(new Vector2(canvasPos.X + x, canvasPos.Y), new Vector2(canvasPos.X + x, canvasPos.Y + canvasSize.Y), gridColor);
            }
            for (float y = 0; y < canvasSize.Y; y += gridScale)
            {
                drawList.AddLine(new Vector2(canvasPos.X, canvasPos.Y + y), new Vector2(canvasPos.X + canvasSize.X, canvasPos.Y + y), gridColor);
            }

            // 1. Vykreslení spojení (Links)
            if (_currentGraph == null) return;
            Dictionary<int, Vector2> pinPositions = CalculatePinPositions(_currentGraph, canvasPos);
            uint linkColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.6f, 0f, 0.8f));
            uint linkTriggerColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.9f, 0.8f));

            for (int i = _currentGraph.Links.Count - 1; i >= 0; i--)
            {
                var link = _currentGraph.Links[i];
                if (pinPositions.TryGetValue(link.FromPinId, out var fromPos) &&
                    pinPositions.TryGetValue(link.ToPinId, out var toPos))
                {
                    // Rozlišení barvy linky
                    var fromPin = FindPinById(_currentGraph, link.FromPinId);
                    bool isTrigger = fromPin != null && fromPin.Type == "Trigger";
                    uint col = isTrigger ? linkTriggerColor : linkColor;

                    drawList.AddBezierCubic(fromPos, fromPos + new Vector2(30, 0), toPos - new Vector2(30, 0), toPos, col, isTrigger ? 2.5f : 1.5f);

                    // Pravý klik na spojení pro smazání
                    Vector2 mid = (fromPos + toPos) * 0.5f;
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && Vector2.Distance(ImGui.GetMousePos(), mid) < 10f)
                    {
                        _currentGraph.Links.RemoveAt(i);
                        SaveGraphToComponent(ref comp);
                    }
                }
            }

            // 2. Vykreslení uzlů (Nodes)
            bool anyNodeHovered = false;
            int nodeToDelete = -1;

            foreach (var node in _currentGraph.Nodes)
            {
                Vector2 nodeMin = canvasPos + new Vector2(node.X, node.Y);
                float nodeWidth = 170f;
                float pinHeight = 22f;
                float headerHeight = 26f;
                float nodeHeight = headerHeight + Math.Max(node.Pins.Count, 1) * pinHeight + 6f;
                Vector2 nodeMax = nodeMin + new Vector2(nodeWidth, nodeHeight);

                // Barva záhlaví podle typu uzlu
                uint headerCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.35f, 1f));
                if (node.Type.StartsWith("Event_"))
                {
                    headerCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.45f, 0.2f, 1f));
                }
                else if (node.Type.StartsWith("Action_"))
                {
                    headerCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.3f, 0.55f, 1f));
                }

                // Kreslení stínu a pozadí uzlu
                drawList.AddRectFilled(nodeMin + new Vector2(2, 2), nodeMax + new Vector2(2, 2), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.3f)), 4f);
                drawList.AddRectFilled(nodeMin, nodeMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.12f, 0.12f, 0.14f, 0.95f)), 4f);
                drawList.AddRectFilled(nodeMin, nodeMin + new Vector2(nodeWidth, headerHeight), headerCol, 4f, ImDrawFlags.RoundCornersTop);
                drawList.AddRect(nodeMin, nodeMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.28f, 1.0f)), 4f);

                // Název uzlu
                drawList.AddText(nodeMin + new Vector2(8, 6), ImGui.ColorConvertFloat4ToU32(Vector4.One), node.Name);

                // Táhnutí uzlu přes záhlaví
                bool mouseOverHeader = ImGui.IsMouseHoveringRect(nodeMin, nodeMin + new Vector2(nodeWidth, headerHeight));
                if (mouseOverHeader)
                {
                    anyNodeHovered = true;
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _draggedPinId == -1)
                    {
                        _draggedNodeId = node.Id;
                        _draggedNodeOffset = ImGui.GetMousePos() - new Vector2(node.X, node.Y);
                    }
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                    {
                        nodeToDelete = node.Id;
                    }
                }

                // Vykreslení pinů
                for (int i = 0; i < node.Pins.Count; i++)
                {
                    var pin = node.Pins[i];
                    float pinY = nodeMin.Y + headerHeight + i * pinHeight + 4f;

                    Vector2 socketPos;
                    bool isInput = pin.Kind == "Input";

                    if (isInput)
                    {
                        socketPos = new Vector2(nodeMin.X + 8f, pinY + 8f);
                        drawList.AddText(new Vector2(nodeMin.X + 18f, pinY + 1f), ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.8f, 1f)), pin.Name);
                    }
                    else
                    {
                        socketPos = new Vector2(nodeMax.X - 8f, pinY + 8f);
                        float txtW = ImGui.CalcTextSize(pin.Name).X;
                        drawList.AddText(new Vector2(nodeMax.X - 18f - txtW, pinY + 1f), ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.8f, 1f)), pin.Name);
                    }

                    // Barva socketu
                    uint socketColor = pin.Type == "Trigger" ? linkTriggerColor : linkColor;
                    drawList.AddCircleFilled(socketPos, 4.5f, socketColor);

                    // Detekce kliknutí/tažení z pinu
                    if (ImGui.IsMouseHoveringRect(socketPos - new Vector2(6, 6), socketPos + new Vector2(6, 6)))
                    {
                        anyNodeHovered = true;
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            _draggedPinId = pin.Id;
                            _draggedPinKind = pin.Kind;
                            _draggedPinType = pin.Type;
                            _draggedPinPos = socketPos;
                        }
                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && _draggedPinId >= 0 && _draggedPinId != pin.Id)
                        {
                            // Pokusíme se propojit
                            var draggedPin = FindPinById(_currentGraph, _draggedPinId);
                            if (draggedPin != null && draggedPin.Kind != pin.Kind && draggedPin.Type == pin.Type)
                            {
                                int fromId = draggedPin.Kind == "Output" ? draggedPin.Id : pin.Id;
                                int toId = draggedPin.Kind == "Input" ? draggedPin.Id : pin.Id;

                                // Zamezíme duplicitním linkům do stejného Inputu
                                _currentGraph.Links.RemoveAll(l => l.ToPinId == toId);

                                _currentGraph.Links.Add(new GraphLink
                                {
                                    Id = _nextId++,
                                    FromPinId = fromId,
                                    ToPinId = toId
                                });
                                SaveGraphToComponent(ref comp);
                            }
                            _draggedPinId = -1;
                        }
                    }

                    // Zobrazení pole pro editaci hodnoty (pouze pro nepropojené Inputy)
                    if (isInput && pin.Type != "Trigger" && !IsPinConnected(_currentGraph, pin.Id))
                    {
                        float valX = nodeMin.X + 85f;
                        float valW = nodeWidth - 95f;
                        ImGui.SetCursorScreenPos(new Vector2(valX, pinY - 1f));
                        ImGui.PushItemWidth(valW);

                        string valBuf = pin.Value ?? "";
                        ImGui.PushID(pin.Id);
                        if (ImGui.InputText("", ref valBuf, 32))
                        {
                            pin.Value = valBuf;
                            SaveGraphToComponent(ref comp);
                        }
                        ImGui.PopID();
                        ImGui.PopItemWidth();
                    }
                }
            }

            // Smazání uzlu pravým klikem
            if (nodeToDelete >= 0)
            {
                _currentGraph.Nodes.RemoveAll(n => n.Id == nodeToDelete);
                // Smazat linky připojené k uzlu
                _currentGraph.Links.RemoveAll(l =>
                {
                    var fPin = FindPinById(_currentGraph, l.FromPinId);
                    var tPin = FindPinById(_currentGraph, l.ToPinId);
                    return fPin == null || tPin == null;
                });
                SaveGraphToComponent(ref comp);
            }

            // Zpracování tažení uzlu
            if (_draggedNodeId >= 0)
            {
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    var node = _currentGraph.Nodes.Find(n => n.Id == _draggedNodeId);
                    if (node != null)
                    {
                        var mPos = ImGui.GetMousePos() - _draggedNodeOffset;
                        // Omezíme pozici na plátno
                        node.X = Math.Max(0f, Math.Min(canvasSize.X - 100f, mPos.X));
                        node.Y = Math.Max(0f, Math.Min(canvasSize.Y - 50f, mPos.Y));
                    }
                }
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    _draggedNodeId = -1;
                    SaveGraphToComponent(ref comp);
                }
            }

            // Zpracování tažení linky
            if (_draggedPinId >= 0)
            {
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    var mPos = ImGui.GetMousePos();
                    drawList.AddLine(_draggedPinPos, mPos, linkTriggerColor, 1.5f);
                }
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    _draggedPinId = -1;
                }
            }

            drawList.PopClipRect();

            // Pravý klik na volné místo plátna – kontextové menu pro přidání uzlů
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && !anyNodeHovered &&
                ImGui.IsMouseHoveringRect(canvasPos, canvasPos + canvasSize))
            {
                ImGui.OpenPopup("CanvasContextMenu");
            }

            if (ImGui.BeginPopup("CanvasContextMenu"))
            {
                Vector2 spawnOffset = ImGui.GetMousePosOnOpeningCurrentPopup() - canvasPos;

                if (ImGui.BeginMenu("Události (Events)"))
                {
                    if (ImGui.MenuItem("On Start")) AddNode(_currentGraph, "Event_OnStart", spawnOffset, ref comp);
                    if (ImGui.MenuItem("On Update")) AddNode(_currentGraph, "Event_OnUpdate", spawnOffset, ref comp);
                    if (ImGui.MenuItem("On Collision")) AddNode(_currentGraph, "Event_OnCollision", spawnOffset, ref comp);
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Akce (Actions)"))
                {
                    if (ImGui.MenuItem("Set Position")) AddNode(_currentGraph, "Action_SetPosition", spawnOffset, ref comp);
                    if (ImGui.MenuItem("Set Light Color")) AddNode(_currentGraph, "Action_SetLightColor", spawnOffset, ref comp);
                    if (ImGui.MenuItem("Play Sound")) AddNode(_currentGraph, "Action_PlaySound", spawnOffset, ref comp);
                    if (ImGui.MenuItem("Add Force (Fyzika)")) AddNode(_currentGraph, "Action_AddForce", spawnOffset, ref comp);
                    ImGui.EndMenu();
                }

                ImGui.EndPopup();
            }
        }

        private void SaveGraphToComponent(ref BehaviorGraphComponent comp)
        {
            try
            {
                comp.GraphJson = System.Text.Json.JsonSerializer.Serialize(_currentGraph, MiniEngine.Engine.SceneJsonContext.Default.BehaviorGraph);
            }
            catch {}
        }

        private bool IsPinConnected(BehaviorGraph graph, int pinId)
        {
            foreach (var link in graph.Links)
            {
                if (link.FromPinId == pinId || link.ToPinId == pinId) return true;
            }
            return false;
        }

        private GraphPin? FindPinById(BehaviorGraph graph, int pinId)
        {
            foreach (var node in graph.Nodes)
            {
                foreach (var pin in node.Pins)
                {
                    if (pin.Id == pinId) return pin;
                }
            }
            return null;
        }

        private Dictionary<int, Vector2> CalculatePinPositions(BehaviorGraph graph, Vector2 canvasPos)
        {
            var positions = new Dictionary<int, Vector2>();
            float headerHeight = 26f;
            float pinHeight = 22f;
            float nodeWidth = 170f;

            foreach (var node in graph.Nodes)
            {
                Vector2 nodeMin = canvasPos + new Vector2(node.X, node.Y);
                for (int i = 0; i < node.Pins.Count; i++)
                {
                    var pin = node.Pins[i];
                    float pinY = nodeMin.Y + headerHeight + i * pinHeight + 12f;

                    if (pin.Kind == "Input")
                    {
                        positions[pin.Id] = new Vector2(nodeMin.X + 8f, pinY);
                    }
                    else
                    {
                        positions[pin.Id] = new Vector2(nodeMin.X + nodeWidth - 8f, pinY);
                    }
                }
            }
            return positions;
        }

        private void RebuildIds()
        {
            int maxId = 1000;
            if (_currentGraph != null)
            {
                foreach (var node in _currentGraph.Nodes)
                {
                    maxId = Math.Max(maxId, node.Id);
                    foreach (var pin in node.Pins) maxId = Math.Max(maxId, pin.Id);
                }
                foreach (var link in _currentGraph.Links) maxId = Math.Max(maxId, link.Id);
            }
            _nextId = maxId + 1;
        }

        private void AddNode(BehaviorGraph graph, string type, Vector2 pos, ref BehaviorGraphComponent comp)
        {
            AddNodeInternal(graph, type, pos.X, pos.Y);
            RebuildIds();
            SaveGraphToComponent(ref comp);
        }

        private void AddNodeInternal(BehaviorGraph graph, string type, float x, float y)
        {
            var node = new GraphNode
            {
                Id = _nextId++,
                Type = type,
                X = x,
                Y = y
            };

            switch (type)
            {
                case "Event_OnStart":
                    node.Name = "On Start 🎬";
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Out", Kind = "Output", Type = "Trigger" });
                    break;
                case "Event_OnUpdate":
                    node.Name = "On Update ⏱️";
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Out", Kind = "Output", Type = "Trigger" });
                    break;
                case "Event_OnCollision":
                    node.Name = "On Collision 💥";
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Out", Kind = "Output", Type = "Trigger" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Collided Entity", Kind = "Output", Type = "Entity", Value = "-1" });
                    break;
                case "Action_SetPosition":
                    node.Name = "Set Position 📍";
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "In", Kind = "Input", Type = "Trigger" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Target", Kind = "Input", Type = "Entity", Value = "Self" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Position", Kind = "Input", Type = "Vector3", Value = "0,0,0" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Out", Kind = "Output", Type = "Trigger" });
                    break;
                case "Action_SetLightColor":
                    node.Name = "Set Light Color 💡";
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "In", Kind = "Input", Type = "Trigger" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Target", Kind = "Input", Type = "Entity", Value = "Self" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Color", Kind = "Input", Type = "Vector3", Value = "1,1,1" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Out", Kind = "Output", Type = "Trigger" });
                    break;
                case "Action_PlaySound":
                    node.Name = "Play Sound 🔊";
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "In", Kind = "Input", Type = "Trigger" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Sound Name", Kind = "Input", Type = "String", Value = "" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Out", Kind = "Output", Type = "Trigger" });
                    break;
                case "Action_AddForce":
                    node.Name = "Add Force 🚀";
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "In", Kind = "Input", Type = "Trigger" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Target", Kind = "Input", Type = "Entity", Value = "Self" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Force", Kind = "Input", Type = "Vector3", Value = "0,5,0" });
                    node.Pins.Add(new GraphPin { Id = _nextId++, Name = "Out", Kind = "Output", Type = "Trigger" });
                    break;
            }

            graph.Nodes.Add(node);
        }
    }
}
