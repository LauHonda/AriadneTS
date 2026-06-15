using System;
using UnityEngine;

namespace AriadneTS.Runtime
{
    public sealed class ScriptPackageRuntimeController : MonoBehaviour
    {
        [SerializeField]
        private ScriptRuntimeHost runtimeHost;

        [SerializeField, TextArea(3, 8)]
        private string packageSigningPublicKey = string.Empty;

        private ScriptPackageReader packageReader;
        private ScriptPackage activePackage;

        public ScriptPackage ActivePackage => activePackage;

        public void RegisterHostHandler(string method, Func<string, string> handler)
        {
            RequireRuntimeHost().RegisterHostHandler(method, handler);
        }

        public bool UnregisterHostHandler(string method)
        {
            return RequireRuntimeHost().UnregisterHostHandler(method);
        }

        public string InvokeScript(string method, string payloadJson = "null")
        {
            return RequireRuntimeHost().InvokeScript(method, payloadJson);
        }

        private void Awake()
        {
            if (runtimeHost == null)
            {
                runtimeHost = GetComponent<ScriptRuntimeHost>();
            }
            if (runtimeHost == null)
            {
                throw new InvalidOperationException("ScriptRuntimeHost is required.");
            }

            runtimeHost.SetAutoStart(false);
            packageReader = new ScriptPackageReader(
                packageSigningPublicKey,
                UnityManifestSerializer.Deserialize);
        }

        public ScriptPackage ValidatePackage(byte[] packageBytes)
        {
            return packageReader.Read(packageBytes);
        }

        public void StartPackage(byte[] packageBytes)
        {
            var package = ValidatePackage(packageBytes);
            runtimeHost.StartPackage(package);
            activePackage = package;
        }

        public void StartPackage(TextAsset packageAsset)
        {
            if (packageAsset == null)
            {
                throw new ArgumentNullException(nameof(packageAsset));
            }

            StartPackage(packageAsset.bytes);
        }

        public void SwitchPackage(byte[] packageBytes)
        {
            // Validation completes before the currently running package is touched.
            var package = ValidatePackage(packageBytes);
            runtimeHost.SwitchPackage(package);
            activePackage = package;
        }

        public void SwitchPackage(TextAsset packageAsset)
        {
            if (packageAsset == null)
            {
                throw new ArgumentNullException(nameof(packageAsset));
            }

            SwitchPackage(packageAsset.bytes);
        }

        public void StopPackage()
        {
            runtimeHost.StopRuntime();
            activePackage = null;
        }

        private ScriptRuntimeHost RequireRuntimeHost()
        {
            if (runtimeHost == null)
            {
                runtimeHost = GetComponent<ScriptRuntimeHost>();
            }

            return runtimeHost ??
                throw new InvalidOperationException("ScriptRuntimeHost is required.");
        }
    }
}
