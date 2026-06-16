using UnityEngine;

namespace AriadneTS.Runtime
{
    public sealed class AriadneTsDemoHostBridge : MonoBehaviour
    {
        [SerializeField]
        private ScriptPackageRuntimeController controller;

        private void Awake()
        {
            RegisterHandlers();
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
                Debug.LogWarning("AriadneTsDemoHostBridge requires ScriptPackageRuntimeController.");
                return;
            }

            controller.RegisterHostHandler("demo.getPlayer", _ =>
                "{\"name\":\"Ariadne\",\"engine\":\"Unity\"}");
        }
    }
}
