using AriadneTS.Runtime;
using UnityEngine;

public sealed class RuntimeSmokeTest : MonoBehaviour
{
    private ScriptRuntime runtime;

    private void Start()
    {
        runtime = new ScriptRuntime(Debug.Log);

        runtime.Evaluate(
            "host.log('Hello from QuickJS inside Unity');",
            "smoke-test.js");

        Debug.Log("Runtime smoke test passed");
    }

    private void OnDestroy()
    {
        runtime?.Dispose();
        runtime = null;
    }
}