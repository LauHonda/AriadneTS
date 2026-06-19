import { assets, scenes } from "./assets/index.js";
import { ActorManager } from "./actors/index.js";
import { ComponentRegistry, TransformComponent } from "./components/index.js";
import { error, log, logger, warn, warning } from "./core/index.js";

export { error, log, logger, warn, warning };
export { assets, scenes };
export { Actor, ActorManager } from "./actors/index.js";
export {
  Component,
  ComponentRegistry,
  TransformComponent,
} from "./components/index.js";
export type {
  AssetDownloadRequest,
  AssetDownloadResult,
  AssetGroupHandle,
  AssetGroupKey,
  ActorAsset,
  AnimationAsset,
  AssetGroupLoadOptions,
  AssetHandle,
  AssetKind,
  AssetKey,
  AssetLoadOptions,
  AssetVerifyRequest,
  AssetVerifyResult,
  AssetsApi,
  AsyncCallbacks,
  AudioAsset,
  BinaryAsset,
  JsonAsset,
  MaterialAsset,
  ProgressInfo,
  SceneAsset,
  SceneHandle,
  SceneKey,
  SceneLoadOptions,
  ScenesApi,
  SpriteAsset,
  TextAsset,
  TextureAsset,
} from "./assets/index.js";
export type {
  ActorCreateContext,
  ActorFactory,
  ActorFactoryContext,
  ActorLifecycle,
  ActorSnapshot,
  ActorTickPayload,
  ActorType,
  Quaternion,
  Vector3,
} from "./actors/index.js";
export type {
  ComponentConstructor,
  ComponentCreateContext,
  ComponentLifecycle,
  ComponentSnapshot,
  ComponentTickPayload,
  ComponentType,
} from "./components/index.js";
export type {
  AsyncBeginResult,
  AsyncBridgeCallbacks,
  AsyncBridgeCompletion,
  AsyncBridgeProgress,
  AsyncBridgeRequest,
  BridgeError,
  BridgeResult,
  Logger,
  LogEntry,
  LogLevel,
} from "./core/index.js";

export {
  handleAsyncCompletion,
  handleAsyncProgress,
} from "./core/index.js";

ComponentRegistry.register(TransformComponent);

export const Ariadne = Object.freeze({
  log,
  warning,
  warn,
  error,
  logger,
  assets,
  scenes,
  actors: ActorManager,
  components: ComponentRegistry,
});
