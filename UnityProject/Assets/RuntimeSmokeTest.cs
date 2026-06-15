using AriadneTS.Runtime;
using UnityEngine;

public sealed class RuntimeSmokeTest : MonoBehaviour
{
    private ScriptRuntime runtime;

    private void Start()
    {
        runtime = new ScriptRuntime(
            Debug.Log,
            hostInvoker: (method, payloadJson) =>
            {
                Debug.Log($"TypeScript called host method '{method}' with {payloadJson}");
                return "{\"engine\":\"Unity\"}";
            });

        runtime.Evaluate(
            "host.log('Hello from QuickJS inside Unity');" +
            "host.log(host.invoke('runtime.identity', null).engine);",
            "smoke-test.js");

        Debug.Log("Runtime smoke test passed");
    }

    private void OnDestroy()
    {
        runtime?.Dispose();
        runtime = null;
    }
}
