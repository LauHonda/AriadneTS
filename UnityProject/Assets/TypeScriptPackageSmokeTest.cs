using AriadneTS.Runtime;
using UnityEngine;

public sealed class TypeScriptPackageSmokeTest : MonoBehaviour
{
    [SerializeField] private ScriptPackageRuntimeController controller;
    [SerializeField] private TextAsset packageAsset;

    private void Start()
    {
        Debug.Log("[AriadneTS Demo] Registering C# host method: demo.getPlayer");
        controller.RegisterHostHandler("demo.getPlayer", payloadJson =>
        {
            Debug.Log($"[AriadneTS Demo] TypeScript called C# with: {payloadJson}");
            return "{\"name\":\"Ariadne\",\"engine\":\"Unity\"}";
        });

        Debug.Log("[AriadneTS Demo] Starting signed TypeScript package");
        controller.StartPackage(packageAsset);

        var result = controller.InvokeScript(
            "demo.greet",
            "{\"message\":\"Hello from C#\"}");
        Debug.Log($"[AriadneTS Demo] C# received TypeScript result: {result}");
    }
}
