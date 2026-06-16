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

- `ScriptRuntimeHost` drives `start`, `update`, `lateUpdate`, `shutdown`,
  Promise jobs, and full-runtime state handoff.
- `ScriptPackageRuntimeController` accepts package bytes and starts or switches
  the host.

The package signing private key must remain outside source control and
application builds. Configure only its `RSA1.<modulus>.<exponent>` public key
in the controller.

## Editor Tools

Open **Tools > AriadneTS > Script Tools** in Unity.

- **Initialize TypeScript Project** copies a default `TypeScript/` folder to
  the Unity project root. The button is disabled once the project contains
  `TypeScript/package.json`, `TypeScript/tsconfig.json`, and `TypeScript/src`.
- **Generate Development Private Key** creates a local RSA private key for
  development signing. Keep this file outside source control.
- **Compile TypeScript And Build Package** compiles `TypeScript/src` and writes
  a signed `typescript-package.bytes` to the selected output path.
- **Private Key PEM** selects the signing private key.
- **Public Key** displays the matching `RSA1.<modulus>.<exponent>` key for
  `ScriptPackageRuntimeController`.
- **Create Or Update Runtime Host In Scene** creates a GameObject with
  `ScriptRuntimeHost`, `ScriptPackageRuntimeController`, and
  `ScriptPackageBootstrapper`, then fills the public key and package asset when
  they are available. It also adds `AriadneTsDemoHostBridge` so the default
  TypeScript template can call `demo.getPlayer` immediately.

The public key is derived from the private key. It does not change when script
files or package contents change. It changes only when you use a different
private key.

The tool uses the local `TypeScript/node_modules/typescript` compiler when
available. It can also use a compatible global TypeScript 5.x install even when
Unity does not inherit your shell `PATH`. Run `npm install` inside the generated
`TypeScript/` folder if no compatible compiler is available.

## Importing As A Developer

A Unity project that imports only this UPM package can start development with
these steps:

1. Install Node.js.
2. Import `com.ariadnets.runtime`.
3. Open **Tools > AriadneTS > Script Tools**.
4. Click **Initialize TypeScript Project**.
5. Run `npm install` in the generated `TypeScript/` folder, or use a global
   TypeScript 5.x install.
6. Select or generate a development private key.
7. Build `typescript-package.bytes`.
8. Click **Create Or Update Runtime Host In Scene**.
9. Enter Play Mode.

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
The default TypeScript template calls `demo.getPlayer` during startup, and the
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
