import { callSync } from "../core/index.js";
import type { Quaternion, Vector3 } from "./types.js";

type TransformProperty =
  | "position"
  | "rotation"
  | "localPosition"
  | "localRotation";

export function createEngineActor(params: {
  readonly id: number;
  readonly type: number;
  readonly name: string;
  readonly position: Vector3;
  readonly rotation: Quaternion;
  readonly localPosition: Vector3;
  readonly localRotation: Quaternion;
  readonly parent: number | null;
}): void {
  callSync("actors.create", {
    ...params,
    hasParent: params.parent !== null,
    parentId: params.parent ?? 0,
  });
}

export function destroyEngineActor(id: number): void {
  callSync("actors.destroy", { id });
}

export function syncActorTransform(
  id: number,
  property: TransformProperty,
  value: Vector3 | Quaternion,
): void {
  callSync("actors.setTransform", { id, property, value });
}

export function syncActorParent(id: number, parent: number | null): void {
  callSync("actors.setParent", {
    id,
    hasParent: parent !== null,
    parentId: parent ?? 0,
  });
}
