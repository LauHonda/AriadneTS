using AriadneTS.Runtime;
using UnityEngine;

public sealed class AriadneTsSampleHostBridge : MonoBehaviour
{
    [SerializeField]
    private ScriptPackageRuntimeController controller;

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponent<ScriptPackageRuntimeController>();
        }

        if (controller == null)
        {
            Debug.LogError("AriadneTsSampleHostBridge requires ScriptPackageRuntimeController.");
            return;
        }

        controller.RegisterHostHandler("demo.getPlayer", _ =>
            "{\"name\":\"Ariadne\",\"engine\":\"Unity\"}");
    }

    public void InvokeGreeting()
    {
        if (controller == null)
        {
            return;
        }

        var response = controller.InvokeScript(
            "demo.greet",
            "{\"message\":\"Unity sample\"}");
        Debug.Log("TypeScript replied: " + response);
    }
}
