using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace AriadneTS.Runtime
{
    public sealed class AriadneAddressablesBridge : MonoBehaviour
    {
        [SerializeField]
        private ScriptPackageRuntimeController controller;

        private readonly Dictionary<string, AsyncOperationHandle<UnityEngine.Object>> assetHandles =
            new Dictionary<string, AsyncOperationHandle<UnityEngine.Object>>(StringComparer.Ordinal);
        private readonly Dictionary<string, AsyncOperationHandle<IList<UnityEngine.Object>>> groupHandles =
            new Dictionary<string, AsyncOperationHandle<IList<UnityEngine.Object>>>(StringComparer.Ordinal);
        private readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> sceneHandles =
            new Dictionary<string, AsyncOperationHandle<SceneInstance>>(StringComparer.Ordinal);

        private void Awake()
        {
            RegisterHandlers();
        }

        private void OnDestroy()
        {
            foreach (var handle in assetHandles.Values)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
            foreach (var handle in groupHandles.Values)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
            assetHandles.Clear();
            groupHandles.Clear();
            foreach (var handle in sceneHandles.Values)
            {
                if (handle.IsValid())
                {
                    Addressables.UnloadSceneAsync(handle);
                }
            }
            sceneHandles.Clear();
        }

        public void RegisterHandlers()
        {
            if (controller == null)
            {
                controller = GetComponent<ScriptPackageRuntimeController>();
            }
            if (controller == null)
            {
                Debug.LogWarning("AriadneAddressablesBridge requires ScriptPackageRuntimeController.");
                return;
            }

            controller.RegisterHostHandler("ariadnets.async.begin", BeginAsync);
            controller.RegisterHostHandler("assets.verifySync", _ => Ok("{\"valid\":true}"));
            controller.RegisterHostHandler("assets.loadSync", LoadAssetSync);
            controller.RegisterHostHandler("assets.loadGroupSync", LoadGroupSync);
            controller.RegisterHostHandler("assets.release", ReleaseAsset);
            controller.RegisterHostHandler("assets.releaseGroup", ReleaseGroup);
            controller.RegisterHostHandler("scenes.loadSync", LoadSceneSync);
            controller.RegisterHostHandler("scenes.unloadSync", UnloadSceneSync);
        }

        private string BeginAsync(string payloadJson)
        {
            var request = ParseAsyncRequest(payloadJson ?? "{}");
            if (request == null || string.IsNullOrWhiteSpace(request.method))
            {
                return Error("InvalidRequest", "Async bridge request is invalid.");
            }

            switch (request.method)
            {
                case "assets.verifyAsync":
                    StartCoroutine(CompleteNextFrame(request, "{\"valid\":true}"));
                    break;
                case "assets.downloadAsync":
                    StartDownload(request);
                    break;
                case "assets.preloadGroupAsync":
                    StartLoadGroup(request, "assets.preloadGroupAsync");
                    break;
                case "assets.loadAsync":
                    StartLoadAsset(request);
                    break;
                case "assets.loadGroupAsync":
                    StartLoadGroup(request, "assets.loadGroupAsync");
                    break;
                case "scenes.preloadAsync":
                    StartScenePreload(request);
                    break;
                case "scenes.loadAsync":
                    StartLoadScene(request);
                    break;
                case "scenes.unloadAsync":
                    StartUnloadScene(request);
                    break;
                default:
                    CompleteError(request.requestId, "NotImplemented",
                        "Addressables bridge method is not implemented: " + request.method);
                    break;
            }

            return Ok("{\"requestId\":" + request.requestId + "}");
        }

        private string LoadAssetSync(string payloadJson)
        {
            var request = JsonUtility.FromJson<AssetRequestEnvelope>(payloadJson ?? "{}");
            if (request == null || string.IsNullOrWhiteSpace(request.key))
            {
                return Error("InvalidRequest", "Asset key is required.");
            }

            var id = AssetId(request.key);
            if (!assetHandles.TryGetValue(id, out var handle) || !handle.IsValid())
            {
                handle = Addressables.LoadAssetAsync<UnityEngine.Object>(request.key);
                assetHandles[id] = handle;
            }

            handle.WaitForCompletion();
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                assetHandles.Remove(id);
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return Error(
                    "AssetLoadFailed",
                    handle.OperationException?.Message ?? "Asset load failed: " + request.key);
            }

            return Ok(AssetHandleJson(request.key, handle.Result));
        }

        private string LoadGroupSync(string payloadJson)
        {
            var request = JsonUtility.FromJson<GroupRequestEnvelope>(payloadJson ?? "{}");
            if (request == null || string.IsNullOrWhiteSpace(request.group))
            {
                return Error("InvalidRequest", "Asset group is required.");
            }

            var id = GroupId(request.group);
            if (!groupHandles.TryGetValue(id, out var handle) || !handle.IsValid())
            {
                handle = Addressables.LoadAssetsAsync<UnityEngine.Object>(request.group, null);
                groupHandles[id] = handle;
            }

            handle.WaitForCompletion();
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                groupHandles.Remove(id);
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return Error(
                    "AssetGroupLoadFailed",
                    handle.OperationException?.Message ?? "Asset group load failed: " + request.group);
            }

            return Ok(GroupHandleJson(request.group, handle.Result));
        }

        private string ReleaseAsset(string payloadJson)
        {
            var request = JsonUtility.FromJson<ReleaseRequestEnvelope>(payloadJson ?? "{}");
            var id = !string.IsNullOrWhiteSpace(request?.id)
                ? request.id
                : AssetId(request?.key ?? string.Empty);
            if (assetHandles.TryGetValue(id, out var handle))
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                assetHandles.Remove(id);
            }

            return Ok("null");
        }

        private string ReleaseGroup(string payloadJson)
        {
            var request = JsonUtility.FromJson<GroupRequestEnvelope>(payloadJson ?? "{}");
            var id = GroupId(request?.group ?? string.Empty);
            if (groupHandles.TryGetValue(id, out var handle))
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                groupHandles.Remove(id);
            }

            return Ok("null");
        }

        private string LoadSceneSync(string payloadJson)
        {
            var request = JsonUtility.FromJson<SceneRequestEnvelope>(payloadJson ?? "{}");
            if (request == null || string.IsNullOrWhiteSpace(request.key))
            {
                return Error("InvalidRequest", "Scene key is required.");
            }

            var id = SceneId(request.key);
            if (!sceneHandles.TryGetValue(id, out var handle) || !handle.IsValid())
            {
                var mode = string.Equals(request.options?.mode, "additive", StringComparison.Ordinal)
                    ? LoadSceneMode.Additive
                    : LoadSceneMode.Single;
                var activateOnLoad = request.options == null || request.options.activateOnLoad;
                handle = Addressables.LoadSceneAsync(request.key, mode, activateOnLoad);
                sceneHandles[id] = handle;
            }

            handle.WaitForCompletion();
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                sceneHandles.Remove(id);
                if (handle.IsValid())
                {
                    Addressables.UnloadSceneAsync(handle);
                }
                return Error(
                    "SceneLoadFailed",
                    handle.OperationException?.Message ?? "Scene load failed: " + request.key);
            }

            return Ok(SceneHandleJson(request.key, true));
        }

        private string UnloadSceneSync(string payloadJson)
        {
            var request = JsonUtility.FromJson<SceneRequestEnvelope>(payloadJson ?? "{}");
            var id = !string.IsNullOrWhiteSpace(request?.id)
                ? request.id
                : SceneId(request?.key ?? string.Empty);
            if (sceneHandles.TryGetValue(id, out var handle))
            {
                if (handle.IsValid())
                {
                    Addressables.UnloadSceneAsync(handle);
                }
                sceneHandles.Remove(id);
            }

            return Ok("null");
        }

        private void StartLoadAsset(AsyncRequest request)
        {
            var assetRequest = JsonUtility.FromJson<AssetRequestEnvelope>(request.paramsJson);
            if (assetRequest == null || string.IsNullOrWhiteSpace(assetRequest.key))
            {
                CompleteError(request.requestId, "InvalidRequest", "Asset key is required.");
                return;
            }

            var handle = Addressables.LoadAssetAsync<UnityEngine.Object>(assetRequest.key);
            var id = AssetId(assetRequest.key);
            assetHandles[id] = handle;
            StartCoroutine(WatchOperation(
                request.requestId,
                handle,
                () => AssetHandleJson(assetRequest.key, handle.Result)));
        }

        private void StartLoadGroup(AsyncRequest request, string method)
        {
            var groupRequest = JsonUtility.FromJson<GroupRequestEnvelope>(request.paramsJson);
            if (groupRequest == null || string.IsNullOrWhiteSpace(groupRequest.group))
            {
                CompleteError(request.requestId, "InvalidRequest", "Asset group is required.");
                return;
            }

            var handle = Addressables.LoadAssetsAsync<UnityEngine.Object>(groupRequest.group, null);
            groupHandles[GroupId(groupRequest.group)] = handle;
            StartCoroutine(WatchOperation(
                request.requestId,
                handle,
                () => GroupHandleJson(groupRequest.group, handle.Result)));
        }

        private void StartDownload(AsyncRequest request)
        {
            var downloadRequest = JsonUtility.FromJson<AssetDownloadRequestEnvelope>(request.paramsJson);
            var key = FirstKey(downloadRequest);
            if (string.IsNullOrWhiteSpace(key))
            {
                CompleteError(request.requestId, "InvalidRequest", "Download requires at least one key or group.");
                return;
            }

            var handle = Addressables.DownloadDependenciesAsync(key);
            StartCoroutine(WatchOperation(
                request.requestId,
                handle,
                () => "{\"downloadedBytes\":0,\"totalBytes\":0}"));
        }

        private void StartScenePreload(AsyncRequest request)
        {
            var sceneRequest = JsonUtility.FromJson<SceneRequestEnvelope>(request.paramsJson);
            if (sceneRequest == null || string.IsNullOrWhiteSpace(sceneRequest.key))
            {
                CompleteError(request.requestId, "InvalidRequest", "Scene key is required.");
                return;
            }

            var handle = Addressables.DownloadDependenciesAsync(sceneRequest.key);
            StartCoroutine(WatchOperation(
                request.requestId,
                handle,
                () => SceneHandleJson(sceneRequest.key, false)));
        }

        private void StartLoadScene(AsyncRequest request)
        {
            var sceneRequest = JsonUtility.FromJson<SceneRequestEnvelope>(request.paramsJson);
            if (sceneRequest == null || string.IsNullOrWhiteSpace(sceneRequest.key))
            {
                CompleteError(request.requestId, "InvalidRequest", "Scene key is required.");
                return;
            }

            var mode = string.Equals(sceneRequest.options?.mode, "additive", StringComparison.Ordinal)
                ? LoadSceneMode.Additive
                : LoadSceneMode.Single;
            var activateOnLoad = sceneRequest.options == null || sceneRequest.options.activateOnLoad;
            var handle = Addressables.LoadSceneAsync(sceneRequest.key, mode, activateOnLoad);
            sceneHandles[SceneId(sceneRequest.key)] = handle;
            StartCoroutine(WatchOperation(
                request.requestId,
                handle,
                () => SceneHandleJson(sceneRequest.key, true)));
        }

        private void StartUnloadScene(AsyncRequest request)
        {
            var sceneRequest = JsonUtility.FromJson<SceneRequestEnvelope>(request.paramsJson);
            var id = !string.IsNullOrWhiteSpace(sceneRequest?.id)
                ? sceneRequest.id
                : SceneId(sceneRequest?.key ?? string.Empty);
            if (!sceneHandles.TryGetValue(id, out var handle))
            {
                CompleteError(request.requestId, "SceneNotLoaded", "Scene is not loaded.");
                return;
            }

            sceneHandles.Remove(id);
            var unloadHandle = Addressables.UnloadSceneAsync(handle);
            StartCoroutine(WatchOperation(request.requestId, unloadHandle, () => "null"));
        }

        private IEnumerator CompleteNextFrame(AsyncRequest request, string resultJson)
        {
            yield return null;
            CompleteSuccess(request.requestId, resultJson);
        }

        private IEnumerator WatchOperation(
            int requestId,
            AsyncOperationHandle handle,
            Func<string> resultJson)
        {
            while (!handle.IsDone)
            {
                SendProgress(requestId, handle.PercentComplete);
                yield return null;
            }

            SendProgress(requestId, 1f);
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                CompleteSuccess(requestId, resultJson());
            }
            else
            {
                CompleteError(
                    requestId,
                    "AddressablesOperationFailed",
                    handle.OperationException?.Message ?? "Addressables operation failed.");
            }
        }

        private void SendProgress(int requestId, float percent)
        {
            controller.InvokeScript(
                "ariadnets.async.progress",
                "{\"requestId\":" + requestId +
                ",\"percent\":" + percent.ToString("R", CultureInfo.InvariantCulture) + "}");
        }

        private void CompleteSuccess(int requestId, string resultJson)
        {
            controller.InvokeScript(
                "ariadnets.async.complete",
                "{\"requestId\":" + requestId + ",\"ok\":true,\"result\":" + resultJson + "}");
        }

        private void CompleteError(int requestId, string code, string message)
        {
            controller.InvokeScript(
                "ariadnets.async.complete",
                "{\"requestId\":" + requestId +
                ",\"ok\":false,\"error\":{\"code\":\"" + EscapeJson(code) +
                "\",\"message\":\"" + EscapeJson(message) + "\"}}");
        }

        private static string FirstKey(AssetDownloadRequestEnvelope request)
        {
            if (request?.groups != null && request.groups.Length > 0)
            {
                return request.groups[0];
            }
            if (request?.keys != null && request.keys.Length > 0)
            {
                return request.keys[0];
            }
            return null;
        }

        private static AsyncRequest ParseAsyncRequest(string payloadJson)
        {
            var header = JsonUtility.FromJson<AsyncRequestHeader>(payloadJson);
            if (header == null)
            {
                return null;
            }

            return new AsyncRequest
            {
                requestId = header.requestId,
                method = header.method ?? string.Empty,
                paramsJson = ExtractJsonProperty(payloadJson, "params") ?? "{}",
            };
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
            if (start >= json.Length)
            {
                return null;
            }

            var end = FindJsonValueEnd(json, start);
            return end > start
                ? json.Substring(start, end - start)
                : null;
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

        private static string AssetHandleJson(string key, UnityEngine.Object asset)
        {
            return "{\"id\":\"" + EscapeJson(AssetId(key)) +
                "\",\"key\":\"" + EscapeJson(key) +
                "\",\"kind\":\"" + EscapeJson(KindOf(key, asset)) +
                "\"}";
        }

        private static string GroupHandleJson(string group, IList<UnityEngine.Object> assets)
        {
            var builder = new StringBuilder();
            builder.Append("{\"group\":\"").Append(EscapeJson(group)).Append("\",\"assets\":[");
            for (var index = 0; index < assets.Count; ++index)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                var asset = assets[index];
                builder.Append(AssetHandleJson(asset != null ? asset.name : group, asset));
            }
            builder.Append("]}");
            return builder.ToString();
        }

        private static string SceneHandleJson(string key, bool loaded)
        {
            return "{\"id\":\"" + EscapeJson(SceneId(key)) +
                "\",\"key\":\"" + EscapeJson(key) +
                "\",\"kind\":\"Scene\",\"loaded\":" +
                (loaded ? "true" : "false") +
                "}";
        }

        private static string KindOf(string key, UnityEngine.Object asset)
        {
            if (asset is GameObject) return "Actor";
            if (asset is Sprite) return "Sprite";
            if (asset is Texture) return "Texture";
            if (asset is Material) return "Material";
            if (asset is AudioClip) return "Audio";
            if (asset is AnimationClip) return "Animation";
            if (asset is TextAsset)
            {
                if (key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "Json";
                if (key.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase) ||
                    key.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) return "Binary";
                return "Text";
            }
            return "Unknown";
        }

        private static string AssetId(string key) => "asset:" + key;
        private static string GroupId(string group) => "asset-group:" + group;
        private static string SceneId(string key) => "scene:" + key;

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: builder.Append(character); break;
                }
            }
            return builder.ToString();
        }

        [Serializable]
        private sealed class AsyncRequestHeader
        {
            public int requestId;
            public string method = string.Empty;
        }

        private sealed class AsyncRequest
        {
            public int requestId;
            public string method = string.Empty;
            public string paramsJson = string.Empty;
        }

        [Serializable]
        private sealed class AssetRequestEnvelope
        {
            public string key = string.Empty;
        }

        [Serializable]
        private sealed class AssetDownloadRequestEnvelope
        {
            public string[] groups;
            public string[] keys;
        }

        [Serializable]
        private sealed class GroupRequestEnvelope
        {
            public string group = string.Empty;
        }

        [Serializable]
        private sealed class ReleaseRequestEnvelope
        {
            public string key = string.Empty;
            public string id = string.Empty;
        }

        [Serializable]
        private sealed class SceneRequestEnvelope
        {
            public string key = string.Empty;
            public string id = string.Empty;
            public SceneLoadOptionsEnvelope options;
        }

        [Serializable]
        private sealed class SceneLoadOptionsEnvelope
        {
            public string mode = "single";
            public bool activateOnLoad = true;
        }
    }
}
