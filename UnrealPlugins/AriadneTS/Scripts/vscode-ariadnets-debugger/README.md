# AriadneTS VSCode Debugger

This folder contains a minimal VSCode Debug Adapter for AriadneTS.

It attaches to the AriadneTS runtime debug endpoint, maps VSCode breakpoints to
the runtime JSON commands, polls runtime status, and reports paused script
locations back to VSCode.

Current capabilities:

- set and clear breakpoints from VSCode
- continue from a paused breakpoint
- show a single AriadneTS thread
- show a basic stack frame at the paused TypeScript source location

Not implemented yet:

- variables
- scopes
- step over / step in / step out
- pause button
- automatic Unity or Unreal process launch

## Run Locally

From the repository root:

```sh
code --extensionDevelopmentPath="$PWD/Tools/vscode-ariadnets-debugger" "$PWD"
```

Then use:

```text
Run and Debug > Attach AriadneTS
```

The default configuration connects to `127.0.0.1:9229` and maps source files
under `${workspaceFolder}/TypeScript`.
