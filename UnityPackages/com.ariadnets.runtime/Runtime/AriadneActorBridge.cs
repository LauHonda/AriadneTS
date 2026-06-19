using System;
using System.Collections.Generic;
using UnityEngine;

#pragma warning disable 0649

namespace AriadneTS.Runtime
{
    public sealed class AriadneActorBridge : MonoBehaviour
    {
        private const int TransformComponentType = 1;

        [SerializeField]
        private ScriptPackageRuntimeController controller;

        [SerializeField]
        private Transform actorRoot;

        private readonly Dictionary<int, GameObject> actors =
            new Dictionary<int, GameObject>();
        private readonly Dictionary<int, AriadneScriptComponent> components =
            new Dictionary<int, AriadneScriptComponent>();
        private readonly Dictionary<int, int> componentActors =
            new Dictionary<int, int>();

        private void Awake()
        {
            RegisterHandlers();
        }

        private void OnDestroy()
        {
            foreach (var actor in actors.Values)
            {
                if (actor != null)
                {
                    Destroy(actor);
                }
            }
            actors.Clear();
            components.Clear();
            componentActors.Clear();
        }

        private void OnValidate()
        {
            if (controller == null)
            {
                controller = GetComponent<ScriptPackageRuntimeController>();
            }
        }

        public void RegisterHandlers()
        {
            if (controller == null)
            {
                controller = GetComponent<ScriptPackageRuntimeController>();
            }
            if (controller == null)
            {
                Debug.LogWarning("AriadneActorBridge requires ScriptPackageRuntimeController.");
                return;
            }

            controller.RegisterHostHandler("actors.create", CreateActor);
            controller.RegisterHostHandler("actors.destroy", DestroyActor);
            controller.RegisterHostHandler("actors.setTransform", SetTransform);
            controller.RegisterHostHandler("actors.setParent", SetParent);
            controller.RegisterHostHandler("components.add", AddComponent);
            controller.RegisterHostHandler("components.remove", RemoveComponent);
            controller.RegisterHostHandler("components.setProperty", SetComponentProperty);
        }

        private string CreateActor(string payloadJson)
        {
            var request = JsonUtility.FromJson<CreateActorRequest>(payloadJson ?? "{}");
            if (request == null || request.id <= 0)
            {
                return Error("InvalidRequest", "Actor id is required.");
            }

            if (actors.TryGetValue(request.id, out var existing) && existing != null)
            {
                return Ok(ActorJson(request.id, existing));
            }

            var actor = new GameObject(string.IsNullOrWhiteSpace(request.name)
                ? "Actor" + request.id
                : request.name);
            actors[request.id] = actor;

            var transform = actor.transform;
            if (request.hasParent)
            {
                if (!TryGetTransform(request.parentId, out var parent))
                {
                    actors.Remove(request.id);
                    Destroy(actor);
                    return Error("ParentNotFound", "Parent actor does not exist: " + request.parentId);
                }
                transform.SetParent(parent, false);
            }
            else if (actorRoot != null)
            {
                transform.SetParent(actorRoot, false);
            }

            transform.position = request.position.ToVector3();
            transform.rotation = request.rotation.ToQuaternion();
            transform.localPosition = request.localPosition.ToVector3();
            transform.localRotation = request.localRotation.ToQuaternion();
            return Ok(ActorJson(request.id, actor));
        }

        private string DestroyActor(string payloadJson)
        {
            var request = JsonUtility.FromJson<ActorIdRequest>(payloadJson ?? "{}");
            if (request == null || request.id <= 0)
            {
                return Error("InvalidRequest", "Actor id is required.");
            }

            if (actors.TryGetValue(request.id, out var actor))
            {
                actors.Remove(request.id);
                RemoveActorComponents(request.id);
                if (actor != null)
                {
                    Destroy(actor);
                }
            }

            return Ok("null");
        }

        private string AddComponent(string payloadJson)
        {
            var request = JsonUtility.FromJson<AddComponentRequest>(payloadJson ?? "{}");
            if (request == null || request.actorId <= 0 || request.componentId <= 0)
            {
                return Error("InvalidRequest", "Actor id and component id are required.");
            }
            if (!actors.TryGetValue(request.actorId, out var actor) || actor == null)
            {
                return Error("ActorNotFound", "Actor does not exist: " + request.actorId);
            }

            componentActors[request.componentId] = request.actorId;
            if (request.type == TransformComponentType)
            {
                return Ok(ComponentJson(request.componentId, request.type));
            }

            var component = actor.AddComponent<AriadneScriptComponent>();
            component.Configure(request.componentId, request.type);
            components[request.componentId] = component;
            return Ok(ComponentJson(request.componentId, request.type));
        }

        private string RemoveComponent(string payloadJson)
        {
            var request = JsonUtility.FromJson<ComponentIdRequest>(payloadJson ?? "{}");
            if (request == null || request.componentId <= 0)
            {
                return Error("InvalidRequest", "Component id is required.");
            }

            componentActors.Remove(request.componentId);
            if (components.TryGetValue(request.componentId, out var component))
            {
                components.Remove(request.componentId);
                if (component != null)
                {
                    Destroy(component);
                }
            }

            return Ok("null");
        }

        private string SetComponentProperty(string payloadJson)
        {
            var request = JsonUtility.FromJson<SetComponentPropertyRequest>(payloadJson ?? "{}");
            if (request == null || request.componentId <= 0 || string.IsNullOrWhiteSpace(request.property))
            {
                return Error("InvalidRequest", "Component id and property are required.");
            }
            if (!components.TryGetValue(request.componentId, out var component) || component == null)
            {
                if (componentActors.ContainsKey(request.componentId))
                {
                    return Ok("null");
                }
                return Error("ComponentNotFound", "Component does not exist: " + request.componentId);
            }

            component.SetProperty(
                request.property,
                ExtractJsonProperty(payloadJson ?? "{}", "value") ?? "null");
            return Ok("null");
        }

        private string SetTransform(string payloadJson)
        {
            var request = JsonUtility.FromJson<SetTransformRequest>(payloadJson ?? "{}");
            if (request == null || request.id <= 0 || string.IsNullOrWhiteSpace(request.property))
            {
                return Error("InvalidRequest", "Actor id and transform property are required.");
            }
            if (!TryGetTransform(request.id, out var transform))
            {
                return Error("ActorNotFound", "Actor does not exist: " + request.id);
            }

            switch (request.property)
            {
                case "position":
                    transform.position = request.value.ToVector3();
                    break;
                case "rotation":
                    transform.rotation = request.value.ToQuaternion();
                    break;
                case "localPosition":
                    transform.localPosition = request.value.ToVector3();
                    break;
                case "localRotation":
                    transform.localRotation = request.value.ToQuaternion();
                    break;
                default:
                    return Error("InvalidProperty", "Unsupported actor transform property: " + request.property);
            }

            return Ok("null");
        }

        private string SetParent(string payloadJson)
        {
            var request = JsonUtility.FromJson<SetParentRequest>(payloadJson ?? "{}");
            if (request == null || request.id <= 0)
            {
                return Error("InvalidRequest", "Actor id is required.");
            }
            if (!TryGetTransform(request.id, out var transform))
            {
                return Error("ActorNotFound", "Actor does not exist: " + request.id);
            }

            if (request.hasParent)
            {
                if (!TryGetTransform(request.parentId, out var parent))
                {
                    return Error("ParentNotFound", "Parent actor does not exist: " + request.parentId);
                }
                transform.SetParent(parent, true);
            }
            else
            {
                transform.SetParent(actorRoot, true);
            }

            return Ok("null");
        }

        private bool TryGetTransform(int actorId, out Transform actorTransform)
        {
            actorTransform = null;
            if (!actors.TryGetValue(actorId, out var actor) || actor == null)
            {
                return false;
            }

            actorTransform = actor.transform;
            return true;
        }

        private void RemoveActorComponents(int actorId)
        {
            var removeIds = new List<int>();
            foreach (var pair in componentActors)
            {
                if (pair.Value == actorId)
                {
                    removeIds.Add(pair.Key);
                }
            }

            foreach (var componentId in removeIds)
            {
                componentActors.Remove(componentId);
                if (components.TryGetValue(componentId, out var component))
                {
                    components.Remove(componentId);
                    if (component != null)
                    {
                        Destroy(component);
                    }
                }
            }
        }

        private static string ActorJson(int id, GameObject actor)
        {
            return "{\"id\":" + id + ",\"name\":\"" + EscapeJson(actor.name) + "\"}";
        }

        private static string ComponentJson(int id, int type)
        {
            return "{\"id\":" + id + ",\"type\":" + type + "}";
        }

        private static string Ok(string resultJson)
        {
            return "{\"ok\":true,\"result\":" + resultJson + "}";
        }

        private static string Error(string code, string message)
        {
            return "{\"ok\":false,\"error\":{\"code\":\"" +
                EscapeJson(code) +
                "\",\"message\":\"" +
                EscapeJson(message) +
                "\"}}";
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static string ExtractJsonProperty(string json, string propertyName)
        {
            var property = "\"" + propertyName + "\"";
            var propertyIndex = json.IndexOf(property, StringComparison.Ordinal);
            if (propertyIndex < 0)
            {
                return null;
            }

            var colonIndex = json.IndexOf(':', propertyIndex + property.Length);
            if (colonIndex < 0)
            {
                return null;
            }

            var start = colonIndex + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
            {
                ++start;
            }

            var end = FindJsonValueEnd(json, start);
            return end > start ? json.Substring(start, end - start) : null;
        }

        private static int FindJsonValueEnd(string json, int start)
        {
            var inString = false;
            var escape = false;
            var depth = 0;
            for (var index = start; index < json.Length; ++index)
            {
                var character = json[index];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (character == '\\')
                    {
                        escape = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                    continue;
                }
                if (character == '{' || character == '[')
                {
                    ++depth;
                    continue;
                }
                if (character == '}' || character == ']')
                {
                    if (depth == 0)
                    {
                        return index;
                    }
                    --depth;
                    if (depth == 0)
                    {
                        return index + 1;
                    }
                    continue;
                }
                if (depth == 0 && character == ',')
                {
                    return index;
                }
            }

            return json.Length;
        }

        [Serializable]
        private sealed class ActorIdRequest
        {
            public int id;
        }

        [Serializable]
        private sealed class CreateActorRequest
        {
            public int id;
            public int type;
            public string name = string.Empty;
            public Vector3Payload position = Vector3Payload.Zero;
            public QuaternionPayload rotation = QuaternionPayload.Identity;
            public Vector3Payload localPosition = Vector3Payload.Zero;
            public QuaternionPayload localRotation = QuaternionPayload.Identity;
            public bool hasParent;
            public int parentId;
        }

        [Serializable]
        private sealed class SetTransformRequest
        {
            public int id;
            public string property = string.Empty;
            public TransformValuePayload value = new TransformValuePayload();
        }

        [Serializable]
        private sealed class SetParentRequest
        {
            public int id;
            public bool hasParent;
            public int parentId;
        }

        [Serializable]
        private sealed class AddComponentRequest
        {
            public int actorId;
            public int componentId;
            public int type;
        }

        [Serializable]
        private sealed class ComponentIdRequest
        {
            public int componentId;
        }

        [Serializable]
        private sealed class SetComponentPropertyRequest
        {
            public int componentId;
            public string property = string.Empty;
        }

        [Serializable]
        private struct Vector3Payload
        {
            public float x;
            public float y;
            public float z;

            public static Vector3Payload Zero => new Vector3Payload();
            public Vector3 ToVector3() => new Vector3(x, y, z);
        }

        [Serializable]
        private struct QuaternionPayload
        {
            public float x;
            public float y;
            public float z;
            public float w;

            public static QuaternionPayload Identity =>
                new QuaternionPayload { w = 1f };

            public Quaternion ToQuaternion() => new Quaternion(x, y, z, w);
        }

        [Serializable]
        private sealed class TransformValuePayload
        {
            public float x;
            public float y;
            public float z;
            public float w = 1f;

            public Vector3 ToVector3() => new Vector3(x, y, z);
            public Quaternion ToQuaternion() => new Quaternion(x, y, z, w);
        }
    }
}

#pragma warning restore 0649
