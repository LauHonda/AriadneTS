# Runtime Architecture

## Ownership boundary

The project owns the runtime ABI, engine adapters, signed script-package
validation, module loader, binding system, and tooling. QuickJS is vendored as
an implementation dependency behind the native ABI and can be replaced without
changing engine adapters.

No QuickJS type may appear in a public header under `Native/include`.

## Initial execution model

- One `ts_runtime` owns one QuickJS runtime and context.
- A runtime is created, evaluated, and destroyed on one engine thread.
- The Unity managed adapter enforces single-thread ownership.
- Host callbacks are synchronous and must not throw across the C ABI.
- Module source is provided by the host; QuickJS does not access the filesystem.
- Promise jobs run only when the host calls `ts_runtime_execute_pending_jobs`.
- Unhandled promise rejections surface as script errors after the job queue drains.
- Initial lifecycle calls use the synchronous JSON dispatcher
  `globalThis.__ariadnets_invoke`.
- TypeScript calls engine APIs through synchronous `host.invoke(method, payload)`.
  Method names and payloads are engine-independent; each engine adapter routes
  them to native framework handlers.
- Reloading initially replaces the complete runtime instead of mutating loaded
  modules in place.
- Unity Addressables remains outside the runtime and submits selected package
  bytes through `ScriptPackageRuntimeController`.

## ABI compatibility

Engine adapters call `ts_runtime_abi_version` before creating a runtime.
Breaking public ABI changes increment the version. New configuration fields
must be appended and guarded by `struct_size`.

## Planned milestones

1. Native evaluation and host logging.
2. Module loading and pending-job execution for promises. Completed.
3. Unity lifecycle host and manual full-runtime reload. Completed.
4. Signed in-memory package verification and atomic stateful switching.
   Unity Addressables owns download, cache, version selection, and rollback.
5. Synchronous engine-independent JSON host bridge. Completed.
6. Asynchronous host requests and generated high-frequency typed bindings.

## Script Package Format

Addressables transports one `typescript-package.bytes` asset:

```text
8 bytes   magic: ARDPKG01
uint32    format version
uint32    manifest byte length
uint32    signature byte length
uint32    file count
bytes     signed manifest JSON
bytes     RSA-SHA256 detached signature
repeat file count:
  uint32  UTF-8 path byte length
  uint64  file byte length
  bytes   UTF-8 path
  bytes   file contents
```

The runtime verifies the signed manifest and every module before exposing a
`ScriptPackage`. Version selection and rollback target selection are not part
of this format or runtime.

The Unity-facing public key uses `RSA1.<Base64Url modulus>.<Base64Url exponent>`
and imports through `RSA.ImportParameters`; it does not depend on Unity support
for ASN.1 SubjectPublicKeyInfo parsing.

## Current limitations

- The JSON host bridge is synchronous and must run on the runtime owner thread.
- JSON invocation is a correctness and low-frequency business path, not the
  final high-frequency binding path.
- Debug probes use source maps to report paused TypeScript locations. Rich
  object inspection and watch expression evaluation are still future debugger
  work.
- Linux and WebGL native plugins are not built.
- Final plugin import behavior and cryptography support must be verified in the
  actual Unity Editor and IL2CPP players.
