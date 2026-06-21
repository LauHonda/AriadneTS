# AriadneTS Script Debugging

AriadneTS has a cross-engine debug configuration model. The native QuickJS
runtime can open a TCP debug endpoint, optionally wait until a client connects
before running the script package, and pause on instrumented `debugger;`
statements.

AriadneTS now includes a business-development debugger layer for VSCode. It is
designed for day-to-day TypeScript gameplay debugging: breakpoints, continue,
step controls, source-mapped stack frames, local snapshots, bounded object
expansion, and simple watch expressions.

## Port Strategy

Each runtime instance computes its port as:

```text
actualPort = debugBasePort + debugInstanceId
```

Recommended defaults:

```text
Server   9229 + 0 = 9229
Client 1 9229 + 1 = 9230
Client 2 9229 + 2 = 9231
Client 3 9229 + 3 = 9232
```

## Unity

Open:

```text
Tools > AriadneTS > Script Tools
```

Use the **Script Debugging** section to configure:

- Enable Debugging
- Protocol
- Host
- Base Port
- Instance Id
- Role
- Wait For Debugger
- Startup Grace Ms

When the scene runtime host is created or updated, these values are written to
`ScriptRuntimeHost`.

To verify the endpoint on macOS or Linux, start Play Mode and run:

```sh
nc 127.0.0.1 9229
```

Use the configured port if you changed **Base Port** or **Instance Id**. A
successful connection prints:

```text
AriadneTS debug endpoint connected.
Commands: status, continue, step, next, stepIn, stepOut, variables, stack, break <file>:<line>, clear <file>:<line>, breakpoints
```

If **Wait For Debugger** is enabled, Play Mode waits until the first client
connects to this port. The current verification endpoint closes the connection
after sending the greeting, so `nc` should return to the terminal prompt by
itself.

If **Wait For Debugger** is disabled, **Startup Grace Ms** gives the VSCode
adapter a short window to connect and apply breakpoints before entry module
evaluation and `onBeginPlay`. This is useful for the normal "Attach first, then
Play Mode" flow.

## Script Breakpoints

Add a standard JavaScript/TypeScript debugger statement:

```ts
debugger;
```

During package build, AriadneTS rewrites generated JavaScript `debugger;`
statements into native debug checkpoints. When a source map is available, the
checkpoint is mapped back to the original TypeScript file, line, and column.
When execution reaches one, Unity or Unreal logs the TypeScript source location
and pauses script execution.

To continue from another terminal:

```sh
printf "continue\n" | nc 127.0.0.1 9229
```

To query runtime state:

```sh
printf "status\n" | nc 127.0.0.1 9229
```

Example paused response:

```json
{"state":"paused","module":"src/bootstrap.js","line":12,"column":0}
```

With source maps enabled, `module` normally points to the original `.ts` file,
for example:

```json
{"state":"paused","module":"src/game-application.ts","line":33,"column":4}
```

Use the configured port if this runtime instance is not using `9229`.

## Dynamic Breakpoints

Built packages also include conservative line probes for common executable
statements. Set a dynamic breakpoint with:

```sh
printf "break src/game-application.ts:33\n" | nc 127.0.0.1 9229
```

List current breakpoints:

```sh
printf "breakpoints\n" | nc 127.0.0.1 9229
```

Clear one:

```sh
printf "clear src/game-application.ts:33\n" | nc 127.0.0.1 9229
```

The current line probe insertion is intentionally conservative. It targets
obvious generated JavaScript statements and skips syntax-sensitive declaration
positions such as imports, exports, object method declarations, and class-like
declarations. If a line does not pause yet, use an explicit `debugger;`
statement as the reliable fallback.

Dynamic line probes are intended for business scripts. AriadneTS SDK files under
`ariadnets-sdk/` are not dynamically probed by default, which keeps framework
startup and frequently used bridge/component code from paying debugger snapshot
costs. Generated source maps still allow SDK frames to appear in call stacks.

## JSON Commands

The endpoint also accepts newline-terminated JSON commands. These are easier to
bridge into a future VSCode Debug Adapter than the text commands.

```sh
printf '{"command":"status"}\n' | nc 127.0.0.1 9229
printf '{"command":"setBreakpoint","module":"src/game-application.ts","line":33}\n' | nc 127.0.0.1 9229
printf '{"command":"listBreakpoints"}\n' | nc 127.0.0.1 9229
printf '{"command":"clearBreakpoint","module":"src/game-application.ts","line":33}\n' | nc 127.0.0.1 9229
printf '{"command":"continue"}\n' | nc 127.0.0.1 9229
```

## VSCode Breakpoints

A minimal VSCode Debug Adapter is included in the Unity UPM package:

```text
Packages/com.ariadnets.runtime/Editor/Tools/vscode-ariadnets-debugger
```

Developers do not need to open the AriadneTS source repository. From the Unity
project:

1. Open **Tools > AriadneTS > Script Tools**.
2. Click **Install VSCode AriadneTS Debugger** once per machine.
3. Click **Create VSCode Debug Config** if `.vscode/launch.json` is not present.
4. Restart VSCode or run **Developer: Reload Window**.
5. Open the Unity project root in VSCode.

The Unity and Unreal editor tools upsert the AriadneTS attach configuration.
Existing non-AriadneTS VSCode debug configurations are preserved.

Use the existing launch configuration:

```text
Run and Debug > Attach AriadneTS
```

Recommended flow:

1. Build the TypeScript package from Unity so line probes are included.
2. Restart Unity after native plugin changes.
3. Enable AriadneTS script debugging in Unity.
4. Use **Install VSCode AriadneTS Debugger And Config** after adapter updates.
5. Set breakpoints in `TypeScript/**/*.ts`.
6. Start **Attach AriadneTS** in VSCode.
7. Start Play Mode.

For `onBeginPlay` breakpoints, either enable **Wait For Debugger** or keep
**Startup Grace Ms** above `0` so VSCode has time to apply the breakpoints before
the lifecycle starts.

The adapter maps VSCode breakpoint paths under `TypeScript` to AriadneTS module
paths, forwards them to the runtime debug endpoint, polls `status`, and reports
paused locations back to VSCode.

If VSCode says `configured type "ariadnets" is not supported`, the AriadneTS
VSCode extension is not loaded yet. Fix it by running **Install VSCode AriadneTS
Debugger** from Unity again, then fully quit and reopen VSCode. In VSCode,
open Extensions and search:

```text
@installed AriadneTS Debugger
```

If it is not listed, the extension was installed to a different VSCode-compatible
editor profile. Set `VSCODE_EXTENSIONS` to that editor's extension directory
before running the installer, or install from the Unity Editor running under the
same user account.

Current VSCode support is intended to cover normal business debugging:

- breakpoints
- continue
- step over, step in, and step out
- one AriadneTS thread
- source-mapped TypeScript stack frames, including generated JavaScript frames
  that can be resolved through `.js.map` files
- local variable snapshots, including `this`, function parameters, and common
  local declarations
- bounded object and array expansion
- simple watch and hover expressions such as `payload.deltaTime`,
  `actor.name`, `items[0]`, and `$runtime.function`
- AriadneTS runtime status scope, including state, pause id, current module,
  function, line, debug endpoint, and TypeScript root

Variable snapshots are captured at AriadneTS probe points. Primitive values are
shown directly, while objects and arrays are copied with a small depth and item
limit so the IDE can expand common values without pulling an entire gameplay
graph into the debugger.

Special JavaScript values are normalized for display. For example, `undefined`,
functions, `bigint`, `symbol`, circular references, and unavailable values are
shown as readable snapshot strings instead of being dropped by JSON transport.

Watch expression support intentionally accepts only snapshot paths. It does not
execute arbitrary JavaScript while the runtime is paused.

Variable editing, live object references, conditional breakpoints, exception
breakpoints, and native memory inspection are future work for the full debugger
protocol.

The automated debug adapter smoke test covers the first-layer delivery target:
breakpoints, step controls, Locals, Watch/evaluate, object expansion, runtime
status, and multi-frame TypeScript stack mapping.

## Unreal

Open:

```text
Project Settings > Plugins > AriadneTS
```

Set the same debug defaults, then use:

```text
Tools > AriadneTS > Initialize TypeScript Workspace
Tools > AriadneTS > Generate Development Private Key
Tools > AriadneTS > Build TypeScript Package
Tools > AriadneTS > Install VSCode Debugger And Config
Tools > AriadneTS > Create Runtime Host
```

The runtime host also supports command-line overrides for multi-client and
server runs:

```text
-AriadneTSDebug
-AriadneTSDebugHost=127.0.0.1
-AriadneTSDebugPort=9230
-AriadneTSDebugBasePort=9229
-AriadneTSDebugInstance=1
-AriadneTSDebugRole=Client
-AriadneTSWaitForDebugger
```

`-AriadneTSDebugPort` sets the final port directly and resets the instance
offset to `0`. `-AriadneTSDebugBasePort` plus
`-AriadneTSDebugInstance` uses the shared port formula.

## Next Debugger Step

The current debugger is source-probe based. The next improvements are richer
object inspection, watch/evaluate support, and deeper protocol compatibility
with standard JavaScript debuggers.
