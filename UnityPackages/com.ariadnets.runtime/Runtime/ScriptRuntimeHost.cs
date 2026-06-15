using System;
using System.Globalization;
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

        private ScriptRuntime runtime;
        private Func<string, string> moduleLoader;

        public bool IsRunning => runtime != null;

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
            StartRuntime();
        }

        public void SwitchPackage(ScriptPackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            SwitchScriptSource(package.LoadModule, package.Manifest.EntryModule);
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
                executionTimeoutMilliseconds);
            try
            {
                runtime.EvaluateModule(entrySource, entryModule);
                runtime.Invoke("start");
                runtime.ExecutePendingJobs(maxJobsPerFrame);
            }
            catch
            {
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

            var stateJson = runtime.Invoke("beforeReload") ?? "null";
            runtime.Dispose();
            runtime = null;

            StartRuntime();
            runtime.Invoke("afterReload", stateJson);
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

            var stateJson = runtime.Invoke("beforeReload") ?? "null";
            var previousLoader = moduleLoader;
            var previousEntryModule = entryModule;
            StopRuntime();

            try
            {
                ConfigureScriptSource(loader, configuredEntryModule);
                StartRuntime();
                runtime.Invoke("afterReload", stateJson);
                runtime.ExecutePendingJobs(maxJobsPerFrame);
            }
            catch
            {
                StopRuntime();
                ConfigureScriptSource(previousLoader, previousEntryModule);
                StartRuntime();
                runtime.Invoke("afterReload", stateJson);
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
                runtime.Invoke("shutdown");
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
                Debug.LogException(exception);
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
                runtime.Invoke("update", DeltaTimeJson(Time.deltaTime));
                runtime.ExecutePendingJobs(maxJobsPerFrame);
            }
            catch (Exception exception)
            {
                enabled = false;
                Debug.LogException(exception);
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
                runtime.Invoke("lateUpdate", DeltaTimeJson(Time.deltaTime));
            }
            catch (Exception exception)
            {
                enabled = false;
                Debug.LogException(exception);
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
                Debug.LogException(exception);
            }
        }

        private static string DeltaTimeJson(float deltaTime)
        {
            return "{\"deltaTime\":" +
                deltaTime.ToString("R", CultureInfo.InvariantCulture) +
                "}";
        }
    }
}
