# AriadneTS

Run shared TypeScript business logic in game engines through a small,
engine-independent QuickJS runtime.

[English](README.md) · [简体中文](Documentation/locales/README.zh-CN.md) ·
[繁體中文](Documentation/locales/README.zh-TW.md) ·
[日本語](Documentation/locales/README.ja.md) ·
[한국어](Documentation/locales/README.ko.md) ·
[Русский](Documentation/locales/README.ru.md) ·
[Español](Documentation/locales/README.es.md)

> Ariadne's thread guided a path through the labyrinth. AriadneTS carries one
> TypeScript business thread through multiple native game-engine hosts.

## Status

AriadneTS currently targets Unity `6000.0.77f1` and includes an Unreal Engine
`5.0+` plugin under `UnrealPlugins/AriadneTS`. Unity has the primary validation
path today; Unreal mirrors the same TypeScript template, package builder,
debugger setup, and runtime concepts, but still needs full validation inside a
real Unreal project before production use.

## Highlights

- TypeScript compiles to standard ES modules executed by QuickJS.
- The native C ABI is independent of Unity and can support future hosts.
- Unity plugins are included for Windows x86_64, macOS Universal, Android
  arm64/x86_64, and iOS arm64.
- Unreal plugin source is included as a separate plugin folder with editor menu
  actions and Project Settings for project integration.
- Signed single-file `.bytes` packages work naturally with Addressables.
- Package validation happens before the active runtime is touched.
- Runtime switching supports `beforeReload` and `afterReload` state handoff.
- Promise jobs, unhandled rejections, memory limits, stack limits, and
  execution timeouts are supported.

## Architecture

```text
TypeScript source
  -> tsc
ES modules
  -> signed package builder
typescript-package.bytes
  -> Unity Addressables selects and loads TextAsset
ScriptPackageRuntimeController validates and switches
  -> native C ABI
QuickJS
```

Addressables owns downloading, caching, version selection, and rollback
policy. AriadneTS owns package verification, module loading, JavaScript
execution, and atomic runtime switching.

## Engine Bridge

Unity registers engine-independent synchronous JSON methods:

```csharp
controller.RegisterHostHandler("player.getName", payloadJson =>
    "{\"name\":\"Ariadne\"}");
```

TypeScript calls them through the shared host API:

```ts
const player = host.invoke("player.getName", null) as { name: string };
```

This bridge is intended for business and framework calls. Generated typed
bindings for high-frequency APIs are a later layer.

## Quick Start

### 1. Install The Unity Package

In Unity Package Manager, choose **Install package from disk** and select:

```text
UnityPackages/com.ariadnets.runtime/package.json
```

### 2. Build A Signed Script Package

Inside Unity, open **Tools > AriadneTS > Script Tools** to initialize a
`TypeScript/` folder and build a signed package.

From the command line:

```sh
./Tools/generate_signing_key.sh ~/.ariadnets/dev-private-key.pem
./Tools/package_script_update.sh 0.1.0 1 ~/.ariadnets/dev-private-key.pem
```

The Addressables-ready output is:

```text
Build/script-packages/0.1.0/typescript-package.bytes
```

Keep the private key secret. Configure only the printed
`RSA1.<modulus>.<exponent>` public key in Unity.

### 3. Start Or Switch A Package

Add `ScriptRuntimeHost` and `ScriptPackageRuntimeController` to a persistent
GameObject. Then load the package through Addressables:

```csharp
TextAsset asset = await Addressables
    .LoadAssetAsync<TextAsset>("ariadnets-package")
    .Task;

controller.StartPackage(asset);
// Later:
controller.SwitchPackage(nextAsset);
```

If a new package fails during startup or state restoration, the previous
runtime is restored. Unity remains responsible for selecting the next
Addressables version.

### Unreal Plugin Preview

Copy or link the plugin folder into an Unreal project:

```text
UnrealPlugins/AriadneTS -> YourProject/Plugins/AriadneTS
```

Then regenerate project files and enable the plugin in Unreal Editor. Configure
defaults in **Project Settings > Plugins > AriadneTS**, then use
`Tools > AriadneTS` to initialize TypeScript, generate keys, build packages,
install the VSCode debugger, create launch config, and create a runtime host.

## TypeScript Lifecycle

The entry module installs `globalThis.__ariadnets_invoke` and handles:

```text
onBeginPlay
onTick
onEndPlay
beforeReload
afterReload
```

See [bootstrap.ts](TypeScript/src/bootstrap.ts) for the reference
implementation.

## Build And Test

```sh
./Tools/test_all.sh
./Tools/build_unity_plugins.sh
```

`build_unity_plugins.sh` builds locally available Unity native plugins and then
syncs the shared native headers/libraries into the Unreal plugin through
`Tools/sync_unreal_native.sh`.

See [Unity deployment](Documentation/unity-deployment.md) and
[runtime architecture](Documentation/runtime-architecture.md) for details.
The included bidirectional example is documented in
[Unity Demo](Documentation/demo.md).
Script error behavior is documented in [Debugging](Documentation/debugging.md).
Script debugger endpoint configuration is documented in
[Script Debugging](Documentation/script-debugging.md).

## Repository Layout

```text
Native/          Engine-independent C ABI and QuickJS backend
TypeScript/      Reference TypeScript business project
UnityPackages/   Unity UPM package and native plugins
UnrealPlugins/   Unreal Engine plugin preview
UnityProject/    Reproducible Unity integration example
Tools/           Build, signing, packaging, and verification tools
ManagedTests/    Managed/native integration tests
```

## Security

- Never commit a signing private key.
- Use separate development, staging, and production keys.
- Treat package validation as authenticity and integrity protection, not as a
  sandbox for hostile JavaScript.
- Report vulnerabilities according to [SECURITY.md](SECURITY.md).

## License

AriadneTS is licensed under the [MIT License](LICENSE). QuickJS notices are
included in the Unity package.
