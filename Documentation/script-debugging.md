# AriadneTS Script Debugging

AriadneTS has a cross-engine debug configuration model. The native QuickJS
runtime can open a TCP debug endpoint, optionally wait until a client connects
before running the script package, and pause on instrumented `debugger;`
statements.

IDE breakpoints, step, and variable inspection are not complete in this stage.
The current endpoint is a minimal command target before the full debugger
protocol is implemented.

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
Commands: status, continue, break <file>:<line>, clear <file>:<line>, breakpoints
Native breakpoint protocol is not implemented yet.
```

If **Wait For Debugger** is enabled, Play Mode waits until the first client
connects to this port. The current verification endpoint closes the connection
after sending the greeting, so `nc` should return to the terminal prompt by
itself. Later debugger protocol support will replace this with a managed
debugger session.

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

Use the existing launch configuration:

```text
Run and Debug > Attach AriadneTS
```

Recommended flow:

1. Build the TypeScript package from Unity so line probes are included.
2. Restart Unity after native plugin changes.
3. Enable AriadneTS script debugging in Unity.
4. Start Play Mode.
5. Start **Attach AriadneTS** in VSCode.
6. Set breakpoints in `TypeScript/**/*.ts`.

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

Current VSCode support is intentionally minimal:

- breakpoints
- continue
- one AriadneTS thread
- source-mapped TypeScript stack frames
- local variable snapshots
- continue, step over, step in, and step out

Variable editing, watch expression evaluation, and full object inspection are
future work.

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
