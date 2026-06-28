# AriadneTS for Unity

This UPM package hosts the engine-independent native runtime inside Unity.
Unity and Addressables own downloading, caching, version selection, and
rollback policy. This package owns script-package verification, module loading,
QuickJS execution, and atomic runtime switching.

## Supported Platforms

The package includes native plugins for:

| Platform | Plugin |
| --- | --- |
| Windows x86_64 | `Runtime/Plugins/x86_64/ariadnets.dll` |
| macOS Universal | `Runtime/Plugins/macOS/libariadnets.dylib` |
| Android arm64-v8a | `Runtime/Plugins/Android/arm64-v8a/libariadnets.so` |
| Android x86_64 | `Runtime/Plugins/Android/x86_64/libariadnets.so` |
| iOS arm64 | `Runtime/Plugins/iOS/libariadnets.a` |

The Windows DLL import settings are restricted to Windows Editor and Win64
Standalone builds. Unity resolves it through `DllImport("ariadnets")`.

## Addressables Boundary

Build output is one signed `typescript-package.bytes` asset. Load it through
Addressables as a `TextAsset`, then submit its bytes:

```csharp
TextAsset asset = await Addressables
    .LoadAssetAsync<TextAsset>("typescript-package")
    .Task;

controller.SwitchPackage(asset);
```

`ScriptPackageRuntimeController` verifies the signature, manifest, ABI version,
module paths, sizes, and SHA-256 hashes before touching the active runtime.
When startup or state restoration for a new package fails,
`ScriptRuntimeHost.SwitchPackage` restores the previous package and state.

Addressables decides which package to load next after a failure.

## Scene Components

- `ScriptRuntimeHost` drives `onBeginPlay`, `onTick`, `onEndPlay`,
  Promise jobs, and full-runtime state handoff.
- `ScriptPackageRuntimeController` accepts package bytes and starts or switches
  the host.

The package signing private key must remain outside source control and
application builds. Configure only its `RSA1.<modulus>.<exponent>` public key
in the controller.

## Editor Tools

Open **Tools > AriadneTS > Script Tools** in Unity.

The window groups setup by ownership:

- **Environment Setup** shows the Unity project root, TypeScript root, Node
  executable, npm executable, TypeScript workspace state, and VSCode launch
  configuration. Node/npm paths are fixed to the project root under
  `AriadneTS/Toolchain/node/`. The section can fetch the official Node.js
  version index, show a version picker with the recommended LTS release, and
  download the selected project Node.js toolchain. It can also diagnose
  Node/npm compatibility, initialize or repair the default `TypeScript/`
  folder, install the local TypeScript compiler with
  `npm install`, and install the AriadneTS VSCode debugger configuration. The
  local compiler is a development/build tool for producing `.bytes` packages;
  it is not required by the game runtime.
- **Package Signing And Build** owns package versioning, the signing private
  key, the derived public key, output path, and the
  **Compile TypeScript And Build Package** action. Keep private keys outside
  source control.
- **Runtime And Debugging** owns script debug settings, synchronization to the
  open scene and `.vscode/launch.json`, and **Create Or Update Runtime Host In
  Scene**. The runtime setup creates a GameObject with `ScriptRuntimeHost`,
  `ScriptPackageRuntimeController`, and `ScriptPackageBootstrapper`, then fills
  the public key and package asset when they are available. It also adds
  `AriadneTsDemoHostBridge` so the default TypeScript template can call
  `demo.getPlayer` immediately.

The public key is derived from the private key. It does not change when script
files or package contents change. It changes only when you use a different
private key.

The tool uses the local `TypeScript/node_modules/typescript` compiler when
available. It can also use a compatible global TypeScript 5.x install, but the
recommended setup is the project Node.js toolchain under
`AriadneTS/Toolchain/node/`. Use **Install/Change Project Node.js Toolchain** to
download the configured target Node.js version into the project, then use
**Diagnose TypeScript Environment** and **Install Local TypeScript Compiler**.
If the selected Node.js installation is paired with an old npm, the tool stops
before install and asks you to select or repair a Node/npm installation whose
versions are compatible with each other. After installing or repairing Node,
select the new `node` executable in **Environment Setup** and retry the
local compiler install.

## Importing As A Developer

A Unity project that imports only this UPM package can start development with
these steps:

1. Import `com.ariadnets.runtime`.
2. Open **Tools > AriadneTS > Script Tools**.
3. In **Environment Setup**, click **Install/Change Project Node.js Toolchain**.
4. In **Environment Setup**, click
   **Initialize TypeScript Project**.
5. In **Environment Setup**, click **Diagnose TypeScript Environment**, then
   **Install Local TypeScript Compiler** if the local compiler is missing.
6. In **Package Signing And Build**, select or generate a development private
   key, then build `typescript-package.bytes`.
7. In **Runtime And Debugging**, click **Create Or Update Runtime Host In
   Scene**.
8. Enter Play Mode.

No repository-level `Tools/` folder is required by consuming Unity projects.
The TypeScript template and package builder live inside this UPM package.

## Rebuilding Native Plugins

Consumers do not need the repository-level native build tools. Maintainers can
rebuild the plugins from the repository root:

```sh
./Tools/build_unity_plugins.sh
```

Windows requires a MinGW-w64 cross compiler named `x86_64-w64-mingw32-gcc`, or
set `MINGW_CC` before running `./Tools/build_windows.sh`.

## Samples

Import **Getting Started** from Unity Package Manager to get
`AriadneTsSampleHostBridge`. Add it to the same GameObject as the runtime host.
The default TypeScript template calls `demo.getPlayer` during `onBeginPlay`, and the
sample bridge returns JSON from C#.

## Bootstrap Component

`ScriptPackageBootstrapper` is a convenience component for scenes that load a
serialized `TextAsset` package. It starts the configured package on `Start()`
and stops it on destroy. For Addressables or custom update flows, load the
`TextAsset` yourself and call `ScriptPackageRuntimeController.StartPackage` or
`SwitchPackage`.

`AriadneTsDemoHostBridge` is only a starter bridge for the generated template.
Remove or replace it after your own C# host API registers the methods used by
your TypeScript business code.

## Addressables Bridge

The package depends on `com.unity.addressables` and includes
`AriadneAddressablesBridge`. The editor setup tool adds it to the runtime
GameObject when the Addressables assembly is available.

The TypeScript SDK exposes synchronous loading and asynchronous callback
operations:

```ts
const cached = Ariadne.assets.loadSync("ui/icon", { kind: "Sprite" });

Ariadne.assets.loadAsync("ui/icon", {
  onProgress(progress) {},
  onComplete(asset) {},
  onError(error) {},
}, { kind: "Sprite" });
```

Synchronous calls start an Addressables operation when needed and wait for it to
complete. Asynchronous calls start Addressables operations and report progress
back to TypeScript callbacks.

Current Unity mappings:

- `assets.downloadAsync` -> `Addressables.DownloadDependenciesAsync`
- `assets.preloadGroupAsync` / `assets.loadGroupAsync` ->
  `Addressables.LoadAssetsAsync`
- `assets.loadAsync` -> `Addressables.LoadAssetAsync`
- `assets.release` / `assets.releaseGroup` -> `Addressables.Release`
- `scenes.preloadAsync` -> `Addressables.DownloadDependenciesAsync`
- `scenes.loadAsync` -> `Addressables.LoadSceneAsync`
- `scenes.unloadAsync` -> `Addressables.UnloadSceneAsync`

## Low-Level API

`ScriptRuntime` can also execute modules supplied by a custom callback. This is
useful for development tools and future engine adapters.

## Host Bridge

Register synchronous JSON handlers before starting a package:

```csharp
controller.RegisterHostHandler("player.getName", payloadJson =>
    "{\"name\":\"Ariadne\"}");
controller.StartPackage(asset);
```

TypeScript calls the same engine-independent method name:

```ts
const player = host.invoke("player.getName", null) as { name: string };
host.log(player.name);
```

Handlers execute on the runtime owner thread. Return valid JSON and do not use
the synchronous bridge for high-frequency per-entity calls.

## Diagnostics

`ScriptRuntimeHost.diagnosticMode` controls script error output:

- `Automatic` uses full diagnostics in Editor/debug builds and compact output
  in release builds.
- `Development` includes payload JSON and full stack details.
- `Release` logs package and method summaries without payloads.

Enable `writeScriptLogFile` to append script diagnostics under
`Application.persistentDataPath/AriadneTS/script.log`.
