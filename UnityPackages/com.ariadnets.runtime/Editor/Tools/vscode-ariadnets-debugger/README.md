# AriadneTS VSCode Debugger

This folder contains the VSCode Debug Adapter for AriadneTS business script debugging.

It attaches to the AriadneTS runtime debug endpoint, maps VSCode breakpoints to
the runtime JSON commands, polls runtime status, and reports paused script
locations back to VSCode.

Current capabilities:

- set and clear breakpoints from VSCode
- continue from a paused breakpoint
- show a single AriadneTS thread
- show source-mapped TypeScript stack frames, including generated JavaScript frames resolved through `.js.map` files
- show local variable snapshots, including `this`, parameters, and common local declarations
- show bounded object and array expansion
- show special JavaScript values such as `undefined`, functions, `bigint`, `symbol`, and circular references as stable snapshot strings
- evaluate simple watch and hover expressions, such as `payload.deltaTime` and `$runtime.function`
- show AriadneTS runtime status scope with state, pause id, current module, function, line, endpoint, and TypeScript root
- step over / step in / step out

Not implemented yet:

- pause button
- editable variables
- live object references or native memory inspection
- conditional and exception breakpoints
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
