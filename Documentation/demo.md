# Unity Demo

The included Unity project demonstrates signed package loading and synchronous
calls in both directions.

The checked-in package is signed with a development-only demo key. Never reuse
that key policy for production packages.

## Run

1. Open `UnityProject` with Unity `6000.0.77f1`.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Open the Console window.
4. Enter Play mode.

The Console shows:

```text
[AriadneTS Demo] Registering C# host method: demo.getPlayer
[AriadneTS Demo] Starting signed TypeScript package
[AriadneTS Demo] TypeScript called C# with: {"requestedBy":"TypeScript"}
TypeScript received C# result: Ariadne on Unity
TypeScript received C# call: Hello from C#
[AriadneTS Demo] C# received TypeScript result: {"reply":"Hello from TypeScript, Hello from C#"}
```

## Relevant Files

- `UnityProject/Assets/TypeScriptPackageSmokeTest.cs` registers C# handlers,
  starts the signed package, and invokes TypeScript.
- `TypeScript/src/game-application.ts` invokes C# and handles the C# request.
- `TypeScript/src/bootstrap.ts` routes methods to the TypeScript application.

## Rebuild The Demo Package

From Unity, use **Tools > AriadneTS > Script Tools**. In **Package Signing And
Build**, set the output path to:

```text
Assets/TypeScript/typescript-package.bytes
```

Or use the command line:

```sh
./Tools/package_script_update.sh 0.2.0 5 /path/to/private-key.pem
```

Copy the generated `typescript-package.bytes` into
`UnityProject/Assets/TypeScript/`, then configure the matching printed public
key on `ScriptPackageRuntimeController`.
