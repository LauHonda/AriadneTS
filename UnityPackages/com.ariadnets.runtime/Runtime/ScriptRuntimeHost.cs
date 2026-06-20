using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace AriadneTS.Runtime
{
    public sealed class ScriptRuntimeHost : MonoBehaviour
    {
        private const string BuiltInLogMethod = "ariadnets.log";
        private static readonly string[] BuiltInResourceMethods =
        {
            "ariadnets.async.begin",
            "assets.verify",
            "assets.verifySync",
            "assets.verifyAsync",
            "assets.download",
            "assets.downloadAsync",
            "assets.preloadGroup",
            "assets.preloadGroupAsync",
            "assets.load",
            "assets.loadSync",
            "assets.loadAsync",
            "assets.loadGroup",
            "assets.loadGroupSync",
            "assets.loadGroupAsync",
            "assets.release",
            "assets.releaseGroup",
            "scenes.preload",
            "scenes.preloadAsync",
            "scenes.load",
            "scenes.loadSync",
            "scenes.loadAsync",
            "scenes.unload",
            "scenes.unloadSync",
            "scenes.unloadAsync",
            "actors.create",
            "actors.destroy",
            "actors.setTransform",
            "actors.setParent",
            "components.add",
            "components.remove",
            "components.setProperty",
        };

        [SerializeField]
        private string entryModule = "bootstrap.js";

        [SerializeField]
        private ulong memoryLimitBytes = 64 * 1024 * 1024;

        [SerializeField]
        private ulong maxStackSizeBytes = 1024 * 1024;

        [SerializeField]
        private uint executionTimeoutMilliseconds = 1000;

        [SerializeField]
        private uint maxJobsPerFrame = 1024;

        [SerializeField]
        private bool autoStart;

        [SerializeField]
        private ScriptDiagnosticMode diagnosticMode = ScriptDiagnosticMode.Automatic;

        [SerializeField]
        private bool writeScriptLogFile = false;

        [SerializeField]
        private string scriptLogFileName = "AriadneTS/script.log";

        [SerializeField]
        private bool enableScriptDebugging = false;

        [SerializeField]
        private ScriptDebugProtocol debugProtocol = ScriptDebugProtocol.ChromeDevTools;

        [SerializeField]
        private string debugHost = "127.0.0.1";

        [SerializeField]
        private int debugBasePort = 9229;

        [SerializeField]
        private int debugInstanceId = 0;

        [SerializeField]
        private string debugRole = "Client";

        [SerializeField]
        private bool waitForDebugger = false;

        private ScriptRuntime runtime;
        private Func<string, string> moduleLoader;
        private ScriptPackageManifest activeManifest;
        private Exception lastReportedException;
        private readonly Dictionary<string, Func<string, string>> hostHandlers =
            new Dictionary<string, Func<string, string>>(StringComparer.Ordinal);

        public bool IsRunning => runtime != null;
        public bool EnableScriptDebugging => enableScriptDebugging;
        public ScriptDebugProtocol DebugProtocol => debugProtocol;
        public string DebugHost => debugHost;
        public int DebugBasePort => debugBasePort;
        public int DebugInstanceId => debugInstanceId;
        public string DebugRole => debugRole;
        public bool WaitForDebugger => waitForDebugger;
        public int DebugPort => ComputeDebugPort(debugBasePort, debugInstanceId);

        public void RegisterHostHandler(string method, Func<string, string> handler)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                throw new ArgumentException("Host method is required.", nameof(method));
            }

            hostHandlers[method] = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public bool UnregisterHostHandler(string method)
        {
            return method != null && hostHandlers.Remove(method);
        }

        public string InvokeScript(string method, string payloadJson = "null")
        {
            if (runtime == null)
            {
                throw new InvalidOperationException("The script runtime is not running.");
            }

            return runtime.Invoke(method, payloadJson);
        }

        public void SetAutoStart(bool value)
        {
            autoStart = value;
        }

        public void ConfigureScriptSource(Func<string, string> loader, string configuredEntryModule)
        {
            if (string.IsNullOrWhiteSpace(configuredEntryModule))
            {
                throw new ArgumentException("Entry module is required.", nameof(configuredEntryModule));
            }

            ConfigureModuleLoader(loader);
            entryModule = configuredEntryModule;
        }

        public void StartPackage(ScriptPackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            ConfigureScriptSource(package.LoadModule, package.Manifest.EntryModule);
            activeManifest = package.Manifest;
            try
            {
                StartRuntime();
            }
            catch
            {
                activeManifest = null;
                throw;
            }
        }

        public void SwitchPackage(ScriptPackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var previousManifest = activeManifest;
            activeManifest = package.Manifest;
            try
            {
                SwitchScriptSource(package.LoadModule, package.Manifest.EntryModule);
            }
            catch
            {
                activeManifest = previousManifest;
                throw;
            }
        }

        public void ConfigureModuleLoader(Func<string, string> loader)
        {
            if (runtime != null)
            {
                throw new InvalidOperationException("Configure the module loader before starting the runtime.");
            }

            moduleLoader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        public void StartRuntime()
        {
            if (runtime != null)
            {
                return;
            }

            if (moduleLoader == null)
            {
                throw new InvalidOperationException(
                    "No script source is configured. Load a ScriptPackage or configure a module loader first.");
            }
            var entrySource = moduleLoader(entryModule);
            if (entrySource == null)
            {
                throw new InvalidOperationException($"Script entry module was not found: {entryModule}");
            }

            RegisterBuiltInHostHandlers();
            ReportPackageConfiguration();
            ReportDebugConfiguration();
            if (enableScriptDebugging)
            {
                Application.runInBackground = true;
            }
            runtime = new ScriptRuntime(
                Debug.Log,
                moduleLoader,
                memoryLimitBytes,
                maxStackSizeBytes,
                executionTimeoutMilliseconds,
                InvokeHost,
                enableScriptDebugging,
                (uint)debugProtocol,
                string.IsNullOrWhiteSpace(debugHost) ? "127.0.0.1" : debugHost,
                (ushort)DebugPort,
                waitForDebugger);
            try
            {
                runtime.EvaluateModule(entrySource, entryModule);
                InvokeRuntimeWithFallback("onBeginPlay", "start");
                runtime.ExecutePendingJobs(maxJobsPerFrame);
            }
            catch (Exception exception)
            {
                ReportScriptException("start", exception);
                runtime.Dispose();
                runtime = null;
                throw;
            }
        }

        public void Reload()
        {
            if (runtime == null)
            {
                StartRuntime();
                return;
            }

            var stateJson = InvokeRuntime("beforeReload", "beforeReload") ?? "null";
            runtime.Dispose();
            runtime = null;

            StartRuntime();
            InvokeRuntime("afterReload", "afterReload", stateJson);
            runtime.ExecutePendingJobs(maxJobsPerFrame);
        }

        public void SwitchScriptSource(Func<string, string> loader, string configuredEntryModule)
        {
            if (loader == null)
            {
                throw new ArgumentNullException(nameof(loader));
            }
            if (string.IsNullOrWhiteSpace(configuredEntryModule))
            {
                throw new ArgumentException("Entry module is required.", nameof(configuredEntryModule));
            }

            if (runtime == null)
            {
                ConfigureScriptSource(loader, configuredEntryModule);
                StartRuntime();
                return;
            }

            var stateJson = InvokeRuntime("beforeReload", "beforeReload") ?? "null";
            var previousLoader = moduleLoader;
            var previousEntryModule = entryModule;
            var previousManifest = activeManifest;
            StopRuntime();

            try
            {
                ConfigureScriptSource(loader, configuredEntryModule);
                StartRuntime();
                InvokeRuntime("afterReload", "afterReload", stateJson);
                runtime.ExecutePendingJobs(maxJobsPerFrame);
            }
            catch
            {
                StopRuntime();
                ConfigureScriptSource(previousLoader, previousEntryModule);
                activeManifest = previousManifest;
                StartRuntime();
                InvokeRuntime("afterReload", "afterReload", stateJson);
                runtime.ExecutePendingJobs(maxJobsPerFrame);
                throw;
            }
        }

        public void StopRuntime()
        {
            if (runtime == null)
            {
                return;
            }

            try
            {
                InvokeRuntimeWithFallback("onEndPlay", "shutdown");
            }
            finally
            {
                runtime.Dispose();
                runtime = null;
            }
        }

        private void Start()
        {
            if (!autoStart)
            {
                return;
            }

            try
            {
                StartRuntime();
            }
            catch (Exception exception)
            {
                enabled = false;
                ReportScriptException("autoStart", exception);
            }
        }

        private void Update()
        {
            if (runtime == null)
            {
                return;
            }

            try
            {
                InvokeRuntimeWithFallback("onTick", "update", DeltaTimeJson(Time.deltaTime));
                runtime.ExecutePendingJobs(maxJobsPerFrame);
            }
            catch (Exception exception)
            {
                enabled = false;
                ReportScriptException("onTick", exception);
            }
        }

        private void LateUpdate()
        {
            // AriadneTS currently exposes onBeginPlay, onTick, and onEndPlay.
            // Frame-end lifecycle can be added later with a cross-engine contract.
        }

        private void OnDestroy()
        {
            if (runtime == null)
            {
                return;
            }

            try
            {
                StopRuntime();
            }
            catch (Exception exception)
            {
                ReportScriptException("destroy", exception);
            }
        }

        private static string DeltaTimeJson(float deltaTime)
        {
            return "{\"deltaTime\":" +
                deltaTime.ToString("R", CultureInfo.InvariantCulture) +
                "}";
        }

        private string InvokeHost(string method, string payloadJson)
        {
            if (!hostHandlers.TryGetValue(method, out var handler))
            {
                throw new InvalidOperationException($"Unknown host method: {method}");
            }

            return handler(payloadJson) ?? "null";
        }

        private void RegisterBuiltInHostHandlers()
        {
            if (!hostHandlers.ContainsKey(BuiltInLogMethod))
            {
                hostHandlers.Add(BuiltInLogMethod, HandleScriptLog);
            }
            foreach (var method in BuiltInResourceMethods)
            {
                if (!hostHandlers.ContainsKey(method))
                {
                    hostHandlers.Add(method, payloadJson => HandleNotImplementedBridge(method, payloadJson));
                }
            }
        }

        private void ReportDebugConfiguration()
        {
            if (!enableScriptDebugging)
            {
                return;
            }

            Debug.LogWarning(
                "AriadneTS script debugging is configured for " +
                debugProtocol +
                " at " +
                (string.IsNullOrWhiteSpace(debugHost) ? "127.0.0.1" : debugHost) +
                ":" +
                DebugPort.ToString(CultureInfo.InvariantCulture) +
                " role=" +
                (string.IsNullOrWhiteSpace(debugRole) ? "Client" : debugRole) +
                " waitForDebugger=" +
                waitForDebugger.ToString(CultureInfo.InvariantCulture) +
                ". A TCP debug endpoint will listen on this address and accepts AriadneTS breakpoint commands.");
        }

        private void ReportPackageConfiguration()
        {
            if (activeManifest == null)
            {
                Debug.Log(
                    "AriadneTS starting script runtime. Entry: " +
                    entryModule +
                    ".");
                return;
            }

            Debug.Log(
                "AriadneTS starting script package " +
                activeManifest.Version +
                " build " +
                activeManifest.BuildNumber.ToString(CultureInfo.InvariantCulture) +
                ". Entry: " +
                activeManifest.EntryModule +
                ". Required ABI: " +
                activeManifest.RequiredRuntimeAbiVersion.ToString(CultureInfo.InvariantCulture) +
                ".");
        }

        private static int ComputeDebugPort(int basePort, int instanceId)
        {
            var port = basePort + instanceId;
            if (port < 1)
            {
                return 1;
            }
            if (port > 65535)
            {
                return 65535;
            }
            return port;
        }

        private static string HandleScriptLog(string payloadJson)
        {
            var payload = JsonUtility.FromJson<ScriptLogPayload>(payloadJson ?? "{}");
            var message = payload != null && payload.message != null
                ? payload.message
                : string.Empty;
            switch (payload?.level)
            {
                case "warning":
                case "warn":
                    Debug.LogWarning(message);
                    break;
                case "error":
                    Debug.LogError(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }

            return "null";
        }

        private static string HandleNotImplementedBridge(string method, string payloadJson)
        {
            return "{\"ok\":false,\"error\":{\"code\":\"NotImplemented\",\"message\":\"Bridge method is not implemented: " +
                EscapeJson(method) +
                "\",\"details\":{\"payload\":" +
                NormalizeJsonPayload(payloadJson) +
                "}}}";
        }

        private static string NormalizeJsonPayload(string payloadJson)
        {
            return string.IsNullOrWhiteSpace(payloadJson)
                ? "null"
                : payloadJson;
        }

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
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }

        private string InvokeRuntime(string phase, string method, string payloadJson = "null")
        {
            return runtime.Invoke(method, payloadJson);
        }

        private string InvokeRuntimeWithFallback(
            string method,
            string legacyMethod,
            string payloadJson = "null")
        {
            try
            {
                return runtime.Invoke(method, payloadJson);
            }
            catch (ScriptRuntimeException exception)
            {
                if (!IsUnknownLifecycleMethod(exception, method))
                {
                    throw;
                }

                return runtime.Invoke(legacyMethod, payloadJson);
            }
        }

        private static bool IsUnknownLifecycleMethod(ScriptRuntimeException exception, string method)
        {
            return exception != null &&
                string.Equals(exception.Status, "ScriptError", StringComparison.Ordinal) &&
                exception.Message != null &&
                exception.Message.Contains(
                    $"Unknown lifecycle method: {method}",
                    StringComparison.Ordinal);
        }

        private void ReportScriptException(
            string phase,
            Exception exception,
            string method = null,
            string payloadJson = null)
        {
            if (exception == null)
            {
                exception = new InvalidOperationException("Script runtime startup failed.");
            }
            if (ReferenceEquals(exception, lastReportedException))
            {
                return;
            }
            lastReportedException = exception;

            var message = IsDevelopmentDiagnostics()
                ? FormatDevelopmentError(phase, exception, method, payloadJson)
                : FormatReleaseError(phase, exception, method);

            Debug.LogError(message);
            if (IsDevelopmentDiagnostics())
            {
                Debug.LogException(exception);
            }
            WriteScriptLog(message);
        }

        [Serializable]
        private sealed class ScriptLogPayload
        {
            public string level = string.Empty;
            public string message = string.Empty;
        }

        private bool IsDevelopmentDiagnostics()
        {
            return diagnosticMode == ScriptDiagnosticMode.Development ||
                (diagnosticMode == ScriptDiagnosticMode.Automatic &&
                    (Debug.isDebugBuild || Application.isEditor));
        }

        private string FormatDevelopmentError(
            string phase,
            Exception exception,
            string method,
            string payloadJson)
        {
            var builder = new StringBuilder();
            builder.AppendLine("AriadneTS Script Error");
            builder.Append("Mode: Development").AppendLine();
            AppendCommonFields(builder, phase, exception, method);
            if (!string.IsNullOrEmpty(payloadJson))
            {
                builder.Append("Payload: ").AppendLine(payloadJson);
            }
            builder.Append("Message: ").AppendLine(exception.Message);
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                builder.AppendLine("Managed Stack:");
                builder.AppendLine(exception.StackTrace);
            }
            return builder.ToString().TrimEnd();
        }

        private string FormatReleaseError(string phase, Exception exception, string method)
        {
            var builder = new StringBuilder();
            builder.AppendLine("AriadneTS Script Error");
            builder.Append("Mode: Release").AppendLine();
            AppendCommonFields(builder, phase, exception, method);
            builder.Append("Message: ").AppendLine(Summarize(exception.Message));
            return builder.ToString().TrimEnd();
        }

        private void AppendCommonFields(
            StringBuilder builder,
            string phase,
            Exception exception,
            string method)
        {
            builder.Append("Phase: ").AppendLine(phase ?? "unknown");
            if (!string.IsNullOrEmpty(method))
            {
                builder.Append("Method: ").AppendLine(method);
            }
            builder.Append("Status: ").AppendLine(
                exception is ScriptRuntimeException scriptException
                    ? scriptException.Status
                    : exception.GetType().Name);
            if (activeManifest != null)
            {
                builder.Append("Package: ")
                    .Append(activeManifest.Version)
                    .Append(" build ")
                    .Append(activeManifest.BuildNumber.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
                builder.Append("Entry: ").AppendLine(activeManifest.EntryModule);
            }
        }

        private static string Summarize(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "Script execution failed.";
            }

            var firstLine = message.Split(new[] { '\r', '\n' }, 2)[0];
            return firstLine.Length <= 240 ? firstLine : firstLine.Substring(0, 240);
        }

        private void WriteScriptLog(string message)
        {
            if (!writeScriptLogFile || string.IsNullOrWhiteSpace(scriptLogFileName))
            {
                return;
            }

            try
            {
                var path = Path.Combine(Application.persistentDataPath, scriptLogFileName);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(
                    path,
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                    Environment.NewLine +
                    message +
                    Environment.NewLine +
                    Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (Exception logException)
            {
                Debug.LogWarning("Failed to write AriadneTS script log: " + logException.Message);
            }
        }
    }
}
