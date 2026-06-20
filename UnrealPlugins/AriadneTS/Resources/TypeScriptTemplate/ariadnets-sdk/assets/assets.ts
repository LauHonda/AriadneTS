import { beginAsync, callSync } from "../core/index.js";
import type {
  AsyncCallbacks,
  AssetDownloadRequest,
  AssetDownloadResult,
  AssetGroupHandle,
  AssetGroupKey,
  AssetGroupLoadOptions,
  AssetHandle,
  AssetKey,
  AssetLoadOptions,
  AssetVerifyRequest,
  AssetVerifyResult,
} from "./types.js";

export interface AssetsApi {
  readonly verifySync: (request: AssetVerifyRequest) => AssetVerifyResult;
  readonly verifyAsync: (
    request: AssetVerifyRequest,
    callbacks?: AsyncCallbacks<AssetVerifyResult>,
  ) => void;
  readonly downloadAsync: (
    request: AssetDownloadRequest,
    callbacks?: AsyncCallbacks<AssetDownloadResult>,
  ) => void;
  readonly preloadGroupAsync: (
    group: AssetGroupKey,
    callbacks?: AsyncCallbacks<AssetGroupHandle>,
  ) => void;
  readonly loadSync: <THandle extends AssetHandle = AssetHandle>(
    key: AssetKey,
    options?: AssetLoadOptions,
  ) => THandle;
  readonly loadAsync: <THandle extends AssetHandle = AssetHandle>(
    key: AssetKey,
    callbacks?: AsyncCallbacks<THandle>,
    options?: AssetLoadOptions,
  ) => void;
  readonly loadGroupSync: (
    group: AssetGroupKey,
    options?: AssetGroupLoadOptions,
  ) => AssetGroupHandle;
  readonly loadGroupAsync: (
    group: AssetGroupKey,
    callbacks?: AsyncCallbacks<AssetGroupHandle>,
    options?: AssetGroupLoadOptions,
  ) => void;
  readonly release: (keyOrHandle: AssetKey | AssetHandle) => void;
  readonly releaseGroup: (group: AssetGroupKey) => void;
}

export const assets: AssetsApi = Object.freeze({
  verifySync(request: AssetVerifyRequest): AssetVerifyResult {
    return callSync("assets.verifySync", request);
  },

  verifyAsync(
    request: AssetVerifyRequest,
    callbacks: AsyncCallbacks<AssetVerifyResult> = {},
  ): void {
    runAsync("assets.verifyAsync", request, callbacks);
  },

  downloadAsync(
    request: AssetDownloadRequest,
    callbacks: AsyncCallbacks<AssetDownloadResult> = {},
  ): void {
    runAsync("assets.downloadAsync", request, callbacks);
  },

  preloadGroupAsync(
    group: AssetGroupKey,
    callbacks: AsyncCallbacks<AssetGroupHandle> = {},
  ): void {
    runAsync("assets.preloadGroupAsync", { group }, callbacks);
  },

  loadSync<THandle extends AssetHandle = AssetHandle>(
    key: AssetKey,
    options: AssetLoadOptions = {},
  ): THandle {
    return callSync("assets.loadSync", { key, options });
  },

  loadAsync<THandle extends AssetHandle = AssetHandle>(
    key: AssetKey,
    callbacks: AsyncCallbacks<THandle> = {},
    options: AssetLoadOptions = {},
  ): void {
    runAsync("assets.loadAsync", { key, options }, callbacks);
  },

  loadGroupSync(
    group: AssetGroupKey,
    options: AssetGroupLoadOptions = {},
  ): AssetGroupHandle {
    return callSync("assets.loadGroupSync", { group, options });
  },

  loadGroupAsync(
    group: AssetGroupKey,
    callbacks: AsyncCallbacks<AssetGroupHandle> = {},
    options: AssetGroupLoadOptions = {},
  ): void {
    runAsync("assets.loadGroupAsync", { group, options }, callbacks);
  },

  release(keyOrHandle: AssetKey | AssetHandle): void {
    const key = typeof keyOrHandle === "string" ? keyOrHandle : keyOrHandle.key;
    const id = typeof keyOrHandle === "string" ? undefined : keyOrHandle.id;
    callSync("assets.release", { key, id });
  },

  releaseGroup(group: AssetGroupKey): void {
    callSync("assets.releaseGroup", { group });
  },
});

function runAsync<TResult>(
  method: string,
  params: unknown,
  callbacks: AsyncCallbacks<TResult>,
): void {
  beginAsync(method, params, callbacks);
}
