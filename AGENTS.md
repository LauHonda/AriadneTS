# AGENTS.md

## Project Direction

AriadneTS is a cross-engine TypeScript runtime framework for game business
logic. The framework must support Unity and Unreal Engine through engine
adapters while keeping TypeScript business APIs engine-neutral.

## Core Requirements

1. Development tooling must support Windows and macOS.
2. Runtime targets must be planned for Windows, macOS, Android, and iOS.
3. Every new feature must consider both Unity and Unreal implementations. Even
   when an issue is discovered in Unity first, check whether Unreal can have the
   same problem and update the Unreal path when applicable.
4. Developer-facing setup must be simple. Prefer engine editor tools, project
   settings, menu actions, and generated defaults over manual terminal steps.
   Configuration effort for developers should stay low.
5. Architecture must stay clean:
   - keep engine-independent runtime logic separated from engine adapters;
   - keep TypeScript business APIs engine-neutral;
   - avoid tight coupling between Unity, Unreal, native runtime, and TS SDK;
   - optimize code during iteration instead of letting temporary code harden;
   - pay attention to runtime efficiency and avoid avoidable overhead.
6. Do not proactively upload or push to GitHub. Only perform GitHub upload or
   push operations when the user explicitly asks.
7. Never commit secrets:
   - private keys;
   - API keys;
   - certificates;
   - tokens;
   - `.env` files;
   - local machine credentials.
   Logs and diagnostic output must not include sensitive information.

## Communication Rules

- Discuss requirements, reasoning, progress, and results with the user in
  Chinese.
- Project implementation content should use standard English unless it is
  developer documentation intended to be localized.
- After modifying, adding, or deleting files, explain which files were changed
  and why.
- When a feature is complete, close the loop with self-testing before handing it
  to the user.
- If a feature requires developers to understand a usage flow, explicitly call
  out the usage steps or key reminders.

## Documentation Rules

Keep development documentation current. Documentation should cover:

- project introduction;
- development environment setup;
- usage instructions;
- API details.

When APIs, editor tools, setup flow, debugging, packaging, or cross-engine
behavior changes, update the relevant documentation in the same work session.

## Validation Expectations

Run focused validation for the changed area before reporting completion. Prefer
the strongest feasible checks in the current environment, such as:

```sh
./Tools/build_native.sh
DYLD_LIBRARY_PATH=Build/native dotnet run --project ManagedTests --no-restore
node --check UnityPackages/com.ariadnets.runtime/Editor/Tools/build_script_package.mjs
git diff --check
```

If Unreal-specific code changes are made but Unreal Editor or Unreal Build Tool
is not available in the current environment, state that clearly and keep the
code structured for later Unreal validation.

## Repository Hygiene

- Do not commit Unity generated folders such as `Library`, `Temp`, `Obj`,
  `Build`, `Builds`, `Logs`, or `UserSettings`.
- Do not commit `node_modules`.
- Do not commit generated TypeScript `dist` folders unless they are explicitly
  required release artifacts.
- Keep Unity UPM package files under `UnityPackages/com.ariadnets.runtime`.
- Keep Unreal plugin files under `UnrealPlugins/AriadneTS`.
- Keep shared TypeScript SDK templates synchronized between Unity and Unreal.
- Keep native runtime changes engine-independent unless an engine-specific
  adapter is intentionally being changed.
