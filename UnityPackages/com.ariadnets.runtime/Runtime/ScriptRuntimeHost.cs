using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
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

        [SerializeField]
        private int debugStartupGraceMilliseconds = 1000;

        private ScriptRuntime runtime;
        private Func<string, string> moduleLoader;
        private ScriptPackageManifest activeManifest;
        private ScriptPackageDebugMetadata activeDebugMetadata;
        private Exception lastReportedException;
        private readonly Dictionary<string, SourceMap> sourceMapCache =
            new Dictionary<string, SourceMap>(StringComparer.Ordinal);
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
        public int DebugStartupGraceMilliseconds => debugStartupGraceMilliseconds;
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
            activeManifest = null;
            activeDebugMetadata = null;
        }

        public void StartPackage(ScriptPackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            ConfigureScriptSource(package.LoadModule, package.Manifest.EntryModule);
            activeManifest = package.Manifest;
            activeDebugMetadata = ParseDebugMetadata(package);
            try
            {
                StartRuntime();
            }
            catch
            {
                activeManifest = null;
                activeDebugMetadata = null;
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
            var previousDebugMetadata = activeDebugMetadata;
            activeManifest = package.Manifest;
            activeDebugMetadata = ParseDebugMetadata(package);
            try
            {
                SwitchScriptSource(package.LoadModule, package.Manifest.EntryModule);
            }
            catch
            {
                activeManifest = previousManifest;
                activeDebugMetadata = previousDebugMetadata;
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
            sourceMapCache.Clear();
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
                WaitForDebugStartupGrace();
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
            var previousDebugMetadata = activeDebugMetadata;
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
                activeDebugMetadata = previousDebugMetadata;
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
                " startupGraceMs=" +
                DebugStartupGraceMilliseconds.ToString(CultureInfo.InvariantCulture) +
                ". A TCP debug endpoint will listen on this address and accepts AriadneTS breakpoint commands.");
        }

        private void WaitForDebugStartupGrace()
        {
            if (!enableScriptDebugging || waitForDebugger || debugStartupGraceMilliseconds <= 0)
            {
                return;
            }

            Thread.Sleep(Math.Min(debugStartupGraceMilliseconds, 5000));
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

        private static ScriptPackageDebugMetadata ParseDebugMetadata(ScriptPackage package)
        {
            if (package == null || string.IsNullOrEmpty(package.DebugMetadataJson))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<ScriptPackageDebugMetadata>(package.DebugMetadataJson);
            }
            catch
            {
                return null;
            }
        }

        private string HandleScriptLog(string payloadJson)
        {
            var payload = JsonUtility.FromJson<ScriptLogPayload>(payloadJson ?? "{}");
            var message = payload != null && payload.message != null
                ? payload.message
                : string.Empty;
            message = MapScriptStack(message);
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
            var scriptMessage = MapScriptStack(exception.Message);
            AppendMappedSourceLocation(builder, scriptMessage);
            builder.Append("Message: ").AppendLine(scriptMessage);
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
            builder.Append("Message: ").AppendLine(Summarize(MapScriptStack(exception.Message)));
            return builder.ToString().TrimEnd();
        }

        private void AppendMappedSourceLocation(StringBuilder builder, string message)
        {
            if (TryFindFirstScriptLocation(message, out var location))
            {
                builder.Append("Source: ")
                    .Append(location.Module)
                    .Append(':')
                    .Append(location.Line.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .AppendLine(location.Column.ToString(CultureInfo.InvariantCulture));
            }
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

        private string MapScriptStack(string message)
        {
            if (string.IsNullOrEmpty(message) || moduleLoader == null)
            {
                return message;
            }

            var lines = message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var changed = false;
            for (var index = 0; index < lines.Length; ++index)
            {
                if (!TryParseStackLine(lines[index], out var prefix, out var module, out var line, out var column, out var suffix))
                {
                    continue;
                }

                var mapped = MapGeneratedLocation(module, line, column);
                if (mapped == null)
                {
                    continue;
                }

                lines[index] = prefix +
                    mapped.Module +
                    ":" +
                    mapped.Line.ToString(CultureInfo.InvariantCulture) +
                    ":" +
                    mapped.Column.ToString(CultureInfo.InvariantCulture) +
                    suffix;
                changed = true;
            }

            return changed ? string.Join("\n", lines) : message;
        }

        private SourceLocation MapGeneratedLocation(string module, int line, int column)
        {
            if (string.IsNullOrEmpty(module) || !module.EndsWith(".js", StringComparison.Ordinal))
            {
                return null;
            }

            var normalizedModule = ScriptPackage.NormalizePath(module);
            var sourceMapPath = activeDebugMetadata?.FindSourceMapPath(normalizedModule) ??
                normalizedModule + ".map";
            var sourceMap = LoadSourceMap(sourceMapPath);
            if (sourceMap == null ||
                sourceMap.sources == null ||
                string.IsNullOrEmpty(sourceMap.mappings))
            {
                return null;
            }

            var mapping = FindBestSourceMapSegment(sourceMap.mappings, line, Math.Max(0, column - 1));
            if (mapping == null ||
                mapping.SourceIndex < 0 ||
                mapping.SourceIndex >= sourceMap.sources.Length)
            {
                return null;
            }

            var source = ResolveSourceMapPath(normalizedModule, sourceMap.sourceRoot, sourceMap.sources[mapping.SourceIndex]);
            return new SourceLocation(source, mapping.OriginalLine + 1, mapping.OriginalColumn + 1);
        }

        private SourceMap LoadSourceMap(string mapModule)
        {
            if (sourceMapCache.TryGetValue(mapModule, out var cached))
            {
                return cached;
            }

            var source = moduleLoader(mapModule);
            if (string.IsNullOrEmpty(source))
            {
                sourceMapCache[mapModule] = null;
                return null;
            }

            try
            {
                var parsed = JsonUtility.FromJson<SourceMap>(source);
                sourceMapCache[mapModule] = parsed;
                return parsed;
            }
            catch
            {
                sourceMapCache[mapModule] = null;
                return null;
            }
        }

        private static string ResolveSourceMapPath(string generatedModule, string sourceRoot, string source)
        {
            var baseDirectory = Path.GetDirectoryName(generatedModule)?.Replace('\\', '/') ?? string.Empty;
            var combined = NormalizeModulePath(
                CombineModulePath(CombineModulePath(baseDirectory, sourceRoot), source));
            while (combined.StartsWith("../", StringComparison.Ordinal))
            {
                combined = combined.Substring(3);
            }
            return combined;
        }

        private static string CombineModulePath(string left, string right)
        {
            if (string.IsNullOrEmpty(left))
            {
                return right ?? string.Empty;
            }
            if (string.IsNullOrEmpty(right))
            {
                return left;
            }
            return left.TrimEnd('/') + "/" + right.TrimStart('/');
        }

        private static string NormalizeModulePath(string path)
        {
            var parts = new List<string>();
            foreach (var part in (path ?? string.Empty).Replace('\\', '/').Split('/'))
            {
                if (string.IsNullOrEmpty(part) || part == ".")
                {
                    continue;
                }
                if (part == "..")
                {
                    if (parts.Count > 0 && parts[parts.Count - 1] != "..")
                    {
                        parts.RemoveAt(parts.Count - 1);
                    }
                    else
                    {
                        parts.Add(part);
                    }
                    continue;
                }
                parts.Add(part);
            }
            return string.Join("/", parts);
        }

        private static bool TryFindFirstScriptLocation(string message, out SourceLocation location)
        {
            location = null;
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            var lines = message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var lineText in lines)
            {
                if (TryParseStackLine(lineText, out _, out var module, out var line, out var column, out _))
                {
                    location = new SourceLocation(module, line, column);
                    return true;
                }
            }
            return false;
        }

        private static bool TryParseStackLine(
            string lineText,
            out string prefix,
            out string module,
            out int line,
            out int column,
            out string suffix)
        {
            prefix = string.Empty;
            module = string.Empty;
            line = 0;
            column = 0;
            suffix = string.Empty;

            if (string.IsNullOrEmpty(lineText))
            {
                return false;
            }

            var secondColon = lineText.LastIndexOf(':');
            if (secondColon <= 0)
            {
                return false;
            }
            var firstColon = lineText.LastIndexOf(':', secondColon - 1);
            if (firstColon <= 0)
            {
                return false;
            }

            var columnEnd = secondColon + 1;
            while (columnEnd < lineText.Length && char.IsDigit(lineText[columnEnd]))
            {
                ++columnEnd;
            }
            if (columnEnd == secondColon + 1 ||
                !int.TryParse(lineText.Substring(secondColon + 1, columnEnd - secondColon - 1), out column) ||
                !int.TryParse(lineText.Substring(firstColon + 1, secondColon - firstColon - 1), out line))
            {
                return false;
            }

            var moduleStart = firstColon - 1;
            while (moduleStart >= 0 &&
                !char.IsWhiteSpace(lineText[moduleStart]) &&
                lineText[moduleStart] != '(')
            {
                --moduleStart;
            }
            ++moduleStart;
            if (moduleStart >= firstColon)
            {
                return false;
            }

            prefix = lineText.Substring(0, moduleStart);
            module = lineText.Substring(moduleStart, firstColon - moduleStart);
            suffix = lineText.Substring(columnEnd);
            return module.EndsWith(".js", StringComparison.Ordinal) ||
                module.EndsWith(".ts", StringComparison.Ordinal);
        }

        private static SourceMapSegment FindBestSourceMapSegment(string mappings, int generatedLine, int generatedColumn)
        {
            var lines = mappings.Split(';');
            var targetLineIndex = generatedLine - 1;
            if (targetLineIndex < 0 || targetLineIndex >= lines.Length)
            {
                return null;
            }

            var sourceIndex = 0;
            var originalLine = 0;
            var originalColumn = 0;
            for (var lineIndex = 0; lineIndex <= targetLineIndex; ++lineIndex)
            {
                var generatedColumnCursor = 0;
                SourceMapSegment best = null;
                foreach (var segment in lines[lineIndex].Split(','))
                {
                    if (string.IsNullOrEmpty(segment))
                    {
                        continue;
                    }

                    var values = DecodeSourceMapSegment(segment);
                    if (values.Count == 0)
                    {
                        continue;
                    }

                    generatedColumnCursor += values[0];
                    if (values.Count >= 4)
                    {
                        sourceIndex += values[1];
                        originalLine += values[2];
                        originalColumn += values[3];
                        if (lineIndex == targetLineIndex && generatedColumnCursor <= generatedColumn)
                        {
                            best = new SourceMapSegment(sourceIndex, originalLine, originalColumn);
                        }
                    }
                }

                if (lineIndex == targetLineIndex)
                {
                    return best;
                }
            }
            return null;
        }

        private static List<int> DecodeSourceMapSegment(string segment)
        {
            var values = new List<int>();
            var index = 0;
            while (index < segment.Length)
            {
                values.Add(DecodeVlq(segment, ref index));
            }
            return values;
        }

        private static int DecodeVlq(string text, ref int index)
        {
            var result = 0;
            var shift = 0;
            while (index < text.Length)
            {
                var digit = SourceMapBase64Value(text[index]);
                ++index;
                result += (digit & 31) << shift;
                if ((digit & 32) == 0)
                {
                    break;
                }
                shift += 5;
            }

            var negative = (result & 1) == 1;
            var value = result >> 1;
            return negative ? -value : value;
        }

        private static int SourceMapBase64Value(char character)
        {
            if (character >= 'A' && character <= 'Z')
            {
                return character - 'A';
            }
            if (character >= 'a' && character <= 'z')
            {
                return character - 'a' + 26;
            }
            if (character >= '0' && character <= '9')
            {
                return character - '0' + 52;
            }
            if (character == '+')
            {
                return 62;
            }
            return character == '/' ? 63 : 0;
        }

#pragma warning disable 0649
        [Serializable]
        private sealed class SourceMap
        {
            public int version;
            public string file;
            public string sourceRoot;
            public string[] sources;
            public string mappings;
        }
#pragma warning restore 0649

        private sealed class SourceLocation
        {
            public SourceLocation(string module, int line, int column)
            {
                Module = module;
                Line = line;
                Column = column;
            }

            public string Module { get; }
            public int Line { get; }
            public int Column { get; }
        }

        private sealed class SourceMapSegment
        {
            public SourceMapSegment(int sourceIndex, int originalLine, int originalColumn)
            {
                SourceIndex = sourceIndex;
                OriginalLine = originalLine;
                OriginalColumn = originalColumn;
            }

            public int SourceIndex { get; }
            public int OriginalLine { get; }
            public int OriginalColumn { get; }
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
