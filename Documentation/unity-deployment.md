# Unity Deployment

Target Unity version: `6000.0.77f1`.

## Responsibilities

Unity Addressables owns:

- remote catalog and version selection
- download, cache, retry, and rollback policy
- loading the selected script package as a `TextAsset`

The AriadneTS Runtime owns:

- signature, ABI, path, size, and SHA-256 verification
- in-memory module indexing
- QuickJS execution
- atomic package switching and state handoff

## Build Runtime And Script Asset

```sh
./Tools/build_unity_plugins.sh
./Tools/generate_signing_key.sh /secure/location/script-package-private-key.pem
./Tools/package_script_update.sh 1.0.0 100 /secure/location/script-package-private-key.pem
```

The Addressables-ready asset is:

```text
Build/script-packages/1.0.0/typescript-package.bytes
```

Keep the private key outside source control and application builds. Record the
printed `RSA1.<modulus>.<exponent>` public key for the Unity inspector.

Changing TypeScript code changes the package bytes and signature, but not the
public key. The public key changes only when the private key changes.

## Unity Editor Tool

The UPM package includes **Tools > AriadneTS > Script Tools**.

1. Click **Initialize TypeScript Project** to create `TypeScript/` at the Unity
   project root. Existing folders are never overwritten.
2. Select a private key PEM.
3. Select an output path, for example
   `Assets/TypeScript/typescript-package.bytes`.
4. Click **Compile TypeScript And Build Package**.
5. Copy the displayed public key into
   `ScriptPackageRuntimeController.packageSigningPublicKey`.

## Deploy Runtime Into Unity

```sh
./Tools/deploy_to_unity.sh /path/to/UnityProject
```

This installs the embedded UPM package under
`Packages/com.ariadnets.runtime`. It deliberately does not copy,
download, cache, or choose a script package.

## Scene Setup

1. Create a persistent GameObject.
2. Add `ScriptRuntimeHost`.
3. Add `ScriptPackageRuntimeController`.
4. Assign the host reference and paste the signing public key.
5. Add `typescript-package.bytes` to the desired Addressables group.
6. Load the selected `TextAsset` with Addressables and call
   `controller.StartPackage(textAsset)` or `controller.SwitchPackage(textAsset)`.

If `SwitchPackage` throws, the previous runtime has already been restored.
Unity can then mark the selected Addressables version invalid and choose its
rollback version.

## Required Unity Validation

- Enter Play Mode and load the initial package through Addressables.
- Switch between two Addressables package versions and verify state handoff.
- Verify invalid signatures and corrupted packages never stop the active runtime.
- Verify failed new-package startup restores the active runtime.
- Build Windows x86_64 and macOS Universal players.
- Build IL2CPP Android arm64 and test on a device.
- Build IL2CPP iOS arm64 and test on a device.
