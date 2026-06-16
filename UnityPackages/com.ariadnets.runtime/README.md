# AriadneTS for Unity

This UPM package hosts the engine-independent native runtime inside Unity.
Unity and Addressables own downloading, caching, version selection, and
rollback policy. This package owns script-package verification, module loading,
QuickJS execution, and atomic runtime switching.

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
