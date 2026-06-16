# Debugging

AriadneTS separates script diagnostics into development and release output.

## Modes

`ScriptRuntimeHost` exposes `diagnosticMode`:

- `Automatic`: development output in the Unity Editor or debug builds, release
  output otherwise.
- `Development`: always output full script diagnostics.
- `Release`: always output compact diagnostics.

Development output includes the lifecycle phase, method name, package version,
build number, payload JSON, error message, and managed stack. Release output
keeps the phase, method, package version, build number, status, and a one-line
message summary. Release mode intentionally omits payloads because they may
contain player data, tokens, orders, or save content.

## Unity Logs

AriadneTS writes through Unity `Debug.LogError` and `Debug.LogException`.
Those messages are included in Unity's normal logs:

- Editor: `Editor.log`
- macOS Player: `~/Library/Logs/<CompanyName>/<ProductName>/Player.log`
- Windows Player: `%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\Player.log`
- Android: `logcat`
- iOS: Xcode or device logs

## Script Log File

Enable `writeScriptLogFile` on `ScriptRuntimeHost` to also append script
diagnostics to:

```text
Application.persistentDataPath/AriadneTS/script.log
```

The path can be changed with `scriptLogFileName`.

## Source Maps

TypeScript source maps are emitted into `TypeScript/dist`. Runtime stack
remapping to `.ts` line and column numbers is planned as the next diagnostic
layer.
