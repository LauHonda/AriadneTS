export type AssetKey = string;
export type AssetGroupKey = string;
export type SceneKey = string;

export type AssetKind =
  | "Unknown"
  | "Texture"
  | "Sprite"
  | "Material"
  | "Actor"
  | "Audio"
  | "Animation"
  | "Scene"
  | "Text"
  | "Binary"
  | "Json";

export interface AssetHandle<TKind extends AssetKind = AssetKind> {
  readonly id: string;
  readonly key: AssetKey;
  readonly kind: TKind;
}

export type TextureAsset = AssetHandle<"Texture">;
export type SpriteAsset = AssetHandle<"Sprite">;
export type MaterialAsset = AssetHandle<"Material">;
export type ActorAsset = AssetHandle<"Actor">;
export type AudioAsset = AssetHandle<"Audio">;
export type AnimationAsset = AssetHandle<"Animation">;
export type SceneAsset = AssetHandle<"Scene">;
export type TextAsset = AssetHandle<"Text">;
export type BinaryAsset = AssetHandle<"Binary">;
export type JsonAsset = AssetHandle<"Json">;

export interface AssetGroupHandle {
  readonly group: AssetGroupKey;
  readonly assets: readonly AssetHandle[];
}

export interface AssetLoadOptions {
  readonly kind?: AssetKind;
}

export interface AssetGroupLoadOptions {
  readonly kind?: AssetKind;
}

export interface AssetVerifyRequest {
  readonly groups?: readonly AssetGroupKey[];
  readonly keys?: readonly AssetKey[];
}

export interface AssetVerifyResult {
  readonly valid: boolean;
  readonly missing?: readonly string[];
  readonly changed?: readonly string[];
}

export interface AssetDownloadRequest {
  readonly groups?: readonly AssetGroupKey[];
  readonly keys?: readonly AssetKey[];
}

export interface AssetDownloadResult {
  readonly downloadedBytes?: number;
  readonly totalBytes?: number;
}

export interface ProgressInfo {
  readonly downloadedBytes?: number;
  readonly totalBytes?: number;
  readonly percent: number;
}

export interface AsyncCallbacks<TResult> {
  readonly onComplete?: (result: TResult) => void;
  readonly onError?: (error: unknown) => void;
  readonly onProgress?: (progress: ProgressInfo) => void;
}

export interface SceneLoadOptions {
  readonly mode?: "single" | "additive";
  readonly activateOnLoad?: boolean;
}

export interface SceneHandle extends AssetHandle<"Scene"> {
  readonly id: string;
  readonly key: SceneKey;
  readonly kind: "Scene";
  readonly loaded: boolean;
}
