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

        private ScriptRuntime runtime;
        private Func<string, string> moduleLoader;
        private ScriptPackageManifest activeManifest;
        private Exception lastReportedException;
        private readonly Dictionary<string, Func<string, string>> hostHandlers =
            new Dictionary<string, Func<string, string>>(StringComparer.Ordinal);

        public bool IsRunning => runtime != null;

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

            runtime = new ScriptRuntime(
                Debug.Log,
                moduleLoader,
                memoryLimitBytes,
                maxStackSizeBytes,
                executionTimeoutMilliseconds,
                InvokeHost);
            try
            {
                runtime.EvaluateModule(entrySource, entryModule);
                runtime.Invoke("start");
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
                InvokeRuntime("shutdown", "shutdown");
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
                InvokeRuntime("update", "update", DeltaTimeJson(Time.deltaTime));
                runtime.ExecutePendingJobs(maxJobsPerFrame);
            }
            catch (Exception exception)
            {
                enabled = false;
                ReportScriptException("update", exception);
            }
        }

        private void LateUpdate()
        {
            if (runtime == null)
            {
                return;
            }

            try
            {
                InvokeRuntime("lateUpdate", "lateUpdate", DeltaTimeJson(Time.deltaTime));
            }
            catch (Exception exception)
            {
                enabled = false;
                ReportScriptException("lateUpdate", exception);
            }
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

        private string InvokeRuntime(string phase, string method, string payloadJson = "null")
        {
            return runtime.Invoke(method, payloadJson);
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
