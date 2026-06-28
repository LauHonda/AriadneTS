# AriadneTS Unreal Plugin

This plugin is the Unreal Engine adapter for AriadneTS. It targets Unreal
Engine 5.0+ and mirrors the Unity package shape:

- native QuickJS-based TypeScript runtime loading
- `.bytes` script package reading
- host method bridge
- Unreal-side Actor and Component bridge
- editor menu entries for TypeScript workspace initialization and package build
- bundled native libraries for Win64, Mac, Android, and iOS

## Install

Copy or link `UnrealPlugins/AriadneTS` into your Unreal project:

```text
YourProject/
  Plugins/
    AriadneTS/
      AriadneTS.uplugin
```

Then regenerate project files and open the project in Unreal Editor.

## Editor Tools

After the plugin is enabled, use:

```text
Tools > AriadneTS Environment Setup
  Install/Change Project Node.js Toolchain
  Diagnose TypeScript Environment
  Initialize TypeScript Workspace
  Install Local TypeScript Compiler
  Install VSCode Debugger And Config

Tools > AriadneTS Package Signing And Build
  Generate Development Private Key
  Build TypeScript Package

Tools > AriadneTS Runtime And Debugging
  Create VSCode Debug Config
  Create Runtime Host
```

These groups mirror the Unity **Script Tools** window: environment setup owns
project Node.js toolchain install, Node/npm diagnostics, workspace
initialization, local TypeScript compiler install, and VSCode setup; package
signing and build owns the private key and `.bytes` package; runtime and
debugging owns launch config and runtime host creation.

For repeatable project setup, use **Install/Change Project Node.js Toolchain** to
download the configured **Node Version** under `AriadneTS/Toolchain/node/` at
the Unreal project root. The default **Node Executable** and **Npm Executable**
settings point there so editor tools use the project toolchain instead of the
global shell environment.

Configure defaults in:

```text
Edit > Project Settings > Plugins > AriadneTS
```

The settings use path tokens so the plugin can be moved between macOS and
Windows projects:

- `{ProjectDir}`
- `{ContentDir}`
- `{PluginDir}`

The editor tools are intended to be the normal setup path for developers. A
developer should not need the AriadneTS source repository after the plugin is
copied into an Unreal project.

## Native Runtime Synchronization

The Unreal plugin consumes the same engine-independent native ABI as Unity.
When the native runtime or Unity native plugin binaries are rebuilt from the
repository, run:

```sh
./Tools/sync_unreal_native.sh
```

This copies the current native headers and platform libraries into
`Source/ThirdParty/AriadneTSNative`.

## Runtime Actor

Add `AriadneTSRuntimeHost` to a level, configure:

- `Package Path`
- `Package Signing Public Key`
- `Entry Module`, normally from the package manifest

The **Create Runtime Host** menu entry fills the package path, public key, and
debug settings from Project Settings.

On BeginPlay the host starts the script runtime and calls the TS lifecycle:

- `onBeginPlay`
- `onTick`
- `onEndPlay`

## Current Notes

The Unreal package reader validates package magic, manifest shape, file sizes,
and SHA-256 hashes. RSA signature verification is kept as a runtime-facing
configuration field and should be completed before production use.
