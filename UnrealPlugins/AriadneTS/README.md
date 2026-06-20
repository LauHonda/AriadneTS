# AriadneTS Unreal Plugin

This plugin is the Unreal Engine adapter for AriadneTS. It targets Unreal
Engine 5.0+ and mirrors the Unity package shape:

- native QuickJS-based TypeScript runtime loading
- `.bytes` script package reading
- host method bridge
- Unreal-side Actor and Component bridge
- editor menu entries for TypeScript workspace initialization and package build

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
Tools > AriadneTS > Initialize TypeScript Workspace
Tools > AriadneTS > Generate Development Private Key
Tools > AriadneTS > Build TypeScript Package
Tools > AriadneTS > Install VSCode Debugger And Config
Tools > AriadneTS > Create VSCode Debug Config
Tools > AriadneTS > Create Runtime Host
```

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
