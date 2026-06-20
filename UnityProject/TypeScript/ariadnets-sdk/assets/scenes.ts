import { beginAsync, callSync } from "../core/index.js";
import type {
  AsyncCallbacks,
  SceneHandle,
  SceneKey,
  SceneLoadOptions,
} from "./types.js";

export interface ScenesApi {
  readonly preloadAsync: (
    key: SceneKey,
    callbacks?: AsyncCallbacks<SceneHandle>,
  ) => void;
  readonly loadSync: (key: SceneKey, options?: SceneLoadOptions) => SceneHandle;
  readonly loadAsync: (
    key: SceneKey,
    callbacks?: AsyncCallbacks<SceneHandle>,
    options?: SceneLoadOptions,
  ) => void;
  readonly unloadSync: (keyOrHandle: SceneKey | SceneHandle) => void;
  readonly unloadAsync: (
    keyOrHandle: SceneKey | SceneHandle,
    callbacks?: AsyncCallbacks<void>,
  ) => void;
}

export const scenes: ScenesApi = Object.freeze({
  preloadAsync(
    key: SceneKey,
    callbacks: AsyncCallbacks<SceneHandle> = {},
  ): void {
    runAsync("scenes.preloadAsync", { key }, callbacks);
  },

  loadSync(key: SceneKey, options: SceneLoadOptions = {}): SceneHandle {
    return callSync("scenes.loadSync", { key, options });
  },

  loadAsync(
    key: SceneKey,
    callbacks: AsyncCallbacks<SceneHandle> = {},
    options: SceneLoadOptions = {},
  ): void {
    runAsync("scenes.loadAsync", { key, options }, callbacks);
  },

  unloadSync(keyOrHandle: SceneKey | SceneHandle): void {
    callSync("scenes.unloadSync", sceneRequest(keyOrHandle));
  },

  unloadAsync(
    keyOrHandle: SceneKey | SceneHandle,
    callbacks: AsyncCallbacks<void> = {},
  ): void {
    runAsync("scenes.unloadAsync", sceneRequest(keyOrHandle), callbacks);
  },
});

function sceneRequest(keyOrHandle: SceneKey | SceneHandle): {
  key: SceneKey;
  id?: string;
} {
  const key = typeof keyOrHandle === "string" ? keyOrHandle : keyOrHandle.key;
  const id = typeof keyOrHandle === "string" ? undefined : keyOrHandle.id;
  return { key, id };
}

function runAsync<TResult>(
  method: string,
  params: unknown,
  callbacks: AsyncCallbacks<TResult>,
): void {
  beginAsync(method, params, callbacks);
}
