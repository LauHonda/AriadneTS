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

AriadneTS currently supports Unity `6000.0.77f1`. Unreal support is planned but
not implemented. The project is experimental and should be validated on target
devices before production use.

## Highlights

- TypeScript compiles to standard ES modules executed by QuickJS.
- The native C ABI is independent of Unity and can support future hosts.
- Unity plugins are included for Windows x86_64, macOS Universal, Android
  arm64/x86_64, and iOS arm64.
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

## TypeScript Lifecycle

The entry module installs `globalThis.__ariadnets_invoke` and handles:

```text
start
update
lateUpdate
beforeReload
afterReload
shutdown
```

See [bootstrap.ts](TypeScript/src/bootstrap.ts) for the reference
implementation.

## Build And Test

```sh
./Tools/test_all.sh
./Tools/build_unity_plugins.sh
```

See [Unity deployment](Documentation/unity-deployment.md) and
[runtime architecture](Documentation/runtime-architecture.md) for details.

## Repository Layout

```text
Native/          Engine-independent C ABI and QuickJS backend
TypeScript/      Reference TypeScript business project
UnityPackages/   Unity UPM package and native plugins
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
