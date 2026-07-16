using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;

namespace MiniEngine.Core
{
    public class BehaviorGraph
    {
        public List<GraphNode> Nodes { get; set; } = new();
        public List<GraphLink> Links { get; set; } = new();

        // Runtime index pro rychlé vyhledávání
        [JsonIgnore]
        private Dictionary<int, GraphPin> _pinsById = new();
        [JsonIgnore]
        private Dictionary<int, GraphNode> _nodesByPinId = new();
        [JsonIgnore]
        private Dictionary<int, List<GraphLink>> _linksFromPin = new();
        [JsonIgnore]
        private Dictionary<int, List<GraphLink>> _linksToPin = new();

        public void InitializeRuntime(World world, int entity, object? systemsContext)
        {
            _pinsById.Clear();
            _nodesByPinId.Clear();
            _linksFromPin.Clear();
            _linksToPin.Clear();

            foreach (var node in Nodes)
            {
                foreach (var pin in node.Pins)
                {
                    _pinsById[pin.Id] = pin;
                    _nodesByPinId[pin.Id] = node;
                }
            }

            foreach (var link in Links)
            {
                if (!_linksFromPin.ContainsKey(link.FromPinId))
                    _linksFromPin[link.FromPinId] = new();
                _linksFromPin[link.FromPinId].Add(link);

                if (!_linksToPin.ContainsKey(link.ToPinId))
                    _linksToPin[link.ToPinId] = new();
                _linksToPin[link.ToPinId].Add(link);
            }
        }

        public void Trigger(string eventName, World world, int entity, object? systemsContext, object? eventData = null)
        {
            if (_pinsById.Count == 0)
            {
                InitializeRuntime(world, entity, systemsContext);
            }

            foreach (var node in Nodes)
            {
                if (node.Type == eventName)
                {
                    if (eventName == "Event_OnCollision" && eventData is int collidedEntity)
                    {
                        var pin = node.Pins.Find(p => p.Name == "Collided Entity");
                        if (pin != null) pin.Value = collidedEntity.ToString();
                    }

                    var triggerOut = node.Pins.Find(p => p.Kind == "Output" && p.Type == "Trigger");
                    if (triggerOut != null)
                    {
                        ExecuteTrigger(triggerOut.Id, world, entity, systemsContext);
                    }
                }
            }
        }

        private void ExecuteTrigger(int fromPinId, World world, int entity, object? systemsContext)
        {
            if (!_linksFromPin.TryGetValue(fromPinId, out var links)) return;

            foreach (var link in links)
            {
                if (!_pinsById.TryGetValue(link.ToPinId, out var toPin)) continue;
                if (!_nodesByPinId.TryGetValue(link.ToPinId, out var toNode)) continue;

                ExecuteNode(toNode, toPin, world, entity, systemsContext);
            }
        }

        private void ExecuteNode(GraphNode node, GraphPin triggeredInputPin, World world, int entity, object? systemsContext)
        {
            if (node.Type.StartsWith("Action_"))
            {
                switch (node.Type)
                {
                    case "Action_SetPosition":
                        {
                            var targetEntity = GetPinValueEntity(node, "Target", world, entity);
                            var pos = GetPinValueVector3(node, "Position", world, entity);
                            var transforms = world.Store<Transform>();
                            if (transforms.Has(targetEntity))
                            {
                                transforms.Get(targetEntity).Position = pos;
                            }
                            break;
                        }
                    case "Action_AddForce":
                        {
                            var targetEntity = GetPinValueEntity(node, "Target", world, entity);
                            var force = GetPinValueVector3(node, "Force", world, entity);
                            var bodies = world.Store<RigidBodyRef>();
                            if (bodies.Has(targetEntity))
                            {
                                var bodyRef = bodies.Get(targetEntity);
                                if (systemsContext is MiniEngine.Physics.PhysicsWorld phys)
                                {
                                    var handle = new BepuPhysics.BodyHandle(bodyRef.BodyHandle);
                                    var bepuBody = phys.Simulation.Bodies[handle];
                                    bepuBody.Velocity.Linear += force;
                                }
                            }
                            break;
                        }
                    case "Action_SetLightColor":
                        {
                            var targetEntity = GetPinValueEntity(node, "Target", world, entity);
                            var color = GetPinValueVector3(node, "Color", world, entity);
                            var lights = world.Store<LightComponent>();
                            if (lights.Has(targetEntity))
                            {
                                lights.Get(targetEntity).Color = color;
                            }
                            break;
                        }
                    case "Action_PlaySound":
                        {
                            if (world.Store<AudioSourceComponent>() is { } srcStore)
                            {
                                if (srcStore.Has(entity))
                                {
                                    ref var asc = ref srcStore.Get(entity);
                                    asc.Active = true;
                                    asc.IsPlaying = false;
                                }
                            }
                            break;
                        }
                }

                var triggerOut = node.Pins.Find(p => p.Kind == "Output" && p.Type == "Trigger");
                if (triggerOut != null)
                {
                    ExecuteTrigger(triggerOut.Id, world, entity, systemsContext);
                }
            }
        }

        private float GetPinValueFloat(GraphNode node, string pinName, World world, int entity)
        {
            var pin = node.Pins.Find(p => p.Name == pinName && p.Kind == "Input");
            if (pin == null) return 0f;

            if (_linksToPin.TryGetValue(pin.Id, out var links) && links.Count > 0)
            {
                var link = links[0];
                if (_pinsById.TryGetValue(link.FromPinId, out var fromPin))
                {
                    return EvaluatePinFloat(fromPin, world, entity);
                }
            }

            float.TryParse(pin.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result);
            return result;
        }

        private Vector3 GetPinValueVector3(GraphNode node, string pinName, World world, int entity)
        {
            var pin = node.Pins.Find(p => p.Name == pinName && p.Kind == "Input");
            if (pin == null) return Vector3.Zero;

            if (_linksToPin.TryGetValue(pin.Id, out var links) && links.Count > 0)
            {
                var link = links[0];
                if (_pinsById.TryGetValue(link.FromPinId, out var fromPin))
                {
                    return EvaluatePinVector3(fromPin, world, entity);
                }
            }

            if (string.IsNullOrEmpty(pin.Value)) return Vector3.Zero;
            try
            {
                var parts = pin.Value.Split(',');
                if (parts.Length >= 3)
                {
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
                    float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z);
                    return new Vector3(x, y, z);
                }
            }
            catch {}
            return Vector3.Zero;
        }

        private int GetPinValueEntity(GraphNode node, string pinName, World world, int entity)
        {
            var pin = node.Pins.Find(p => p.Name == pinName && p.Kind == "Input");
            if (pin == null) return entity;

            if (_linksToPin.TryGetValue(pin.Id, out var links) && links.Count > 0)
            {
                var link = links[0];
                if (_pinsById.TryGetValue(link.FromPinId, out var fromPin))
                {
                    return EvaluatePinEntity(fromPin, world, entity);
                }
            }

            if (pin.Value == "Self" || string.IsNullOrEmpty(pin.Value)) return entity;
            int.TryParse(pin.Value, out int result);
            return result;
        }

        private string GetPinValueString(GraphNode node, string pinName)
        {
            var pin = node.Pins.Find(p => p.Name == pinName && p.Kind == "Input");
            return pin?.Value ?? "";
        }

        private float EvaluatePinFloat(GraphPin pin, World world, int entity)
        {
            if (!_nodesByPinId.TryGetValue(pin.Id, out var node)) return 0f;
            float.TryParse(pin.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result);
            return result;
        }

        private Vector3 EvaluatePinVector3(GraphPin pin, World world, int entity)
        {
            if (!_nodesByPinId.TryGetValue(pin.Id, out var node)) return Vector3.Zero;
            return Vector3.Zero;
        }

        private int EvaluatePinEntity(GraphPin pin, World world, int entity)
        {
            if (!_nodesByPinId.TryGetValue(pin.Id, out var node)) return entity;
            if (pin.Value == "Self") return entity;
            int.TryParse(pin.Value, out int result);
            return result;
        }
    }

    public class GraphNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public List<GraphPin> Pins { get; set; } = new();
    }

    public class GraphPin
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "Input"; // Input, Output
        public string Type { get; set; } = "Trigger"; // Trigger, Float, Vector3, Entity, String
        public string Value { get; set; } = "";
    }

    public class GraphLink
    {
        public int Id { get; set; }
        public int FromPinId { get; set; }
        public int ToPinId { get; set; }
    }
}
