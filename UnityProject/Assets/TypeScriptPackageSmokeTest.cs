using AriadneTS.Runtime;
using UnityEngine;

public sealed class TypeScriptPackageSmokeTest : MonoBehaviour
{
    [SerializeField] private ScriptPackageRuntimeController controller;
    [SerializeField] private TextAsset packageAsset;

    private void Start()
    {
        controller.StartPackage(packageAsset);
        Debug.Log("Signed TypeScript package started");
    }
}