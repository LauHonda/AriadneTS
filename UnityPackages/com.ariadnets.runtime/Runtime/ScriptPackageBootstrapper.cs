using System;
using UnityEngine;

namespace AriadneTS.Runtime
{
    public sealed class ScriptPackageBootstrapper : MonoBehaviour
    {
        [SerializeField]
        private ScriptPackageRuntimeController controller;

        [SerializeField]
        private TextAsset packageAsset;

        [SerializeField]
        private bool startOnStart = true;

        [SerializeField]
        private bool stopOnDestroy = true;

        public TextAsset PackageAsset => packageAsset;

        public void ConfigurePackage(TextAsset asset)
        {
            packageAsset = asset;
        }

        public void StartConfiguredPackage()
        {
            if (packageAsset == null)
            {
                throw new InvalidOperationException("AriadneTS package asset is not assigned.");
            }

            RequireController().StartPackage(packageAsset);
        }

        public void SwitchConfiguredPackage()
        {
            if (packageAsset == null)
            {
                throw new InvalidOperationException("AriadneTS package asset is not assigned.");
            }

            RequireController().SwitchPackage(packageAsset);
        }

        private void Start()
        {
            if (startOnStart)
            {
                StartConfiguredPackage();
            }
        }

        private void OnDestroy()
        {
            if (stopOnDestroy && controller != null)
            {
                controller.StopPackage();
            }
        }

        private ScriptPackageRuntimeController RequireController()
        {
            if (controller == null)
            {
                controller = GetComponent<ScriptPackageRuntimeController>();
            }

            return controller ??
                throw new InvalidOperationException("ScriptPackageRuntimeController is required.");
        }
    }
}
