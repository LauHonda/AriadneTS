import { syncActorParent, syncActorTransform } from "../actors/bridge.js";
import type { Quaternion, Vector3 } from "../actors/types.js";
import { Component } from "./component.js";
import type { ComponentCreateContext } from "./types.js";

export class TransformComponent extends Component {
  static override readonly componentType = 1;

  private positionValue: Vector3;
  private rotationValue: Quaternion;
  private localPositionValue: Vector3;
  private localRotationValue: Quaternion;
  private parentValue: number | null;

  constructor(context: ComponentCreateContext) {
    super(context);
    const props = context.props as Partial<TransformState> | undefined;
    this.positionValue = cloneVector3(props?.position ?? zeroVector3());
    this.rotationValue = cloneQuaternion(props?.rotation ?? identityQuaternion());
    this.localPositionValue = cloneVector3(props?.localPosition ?? this.positionValue);
    this.localRotationValue = cloneQuaternion(props?.localRotation ?? this.rotationValue);
    this.parentValue = props?.parent ?? null;
  }

  get position(): Vector3 {
    return cloneVector3(this.positionValue);
  }

  set position(value: Vector3) {
    this.positionValue = cloneVector3(value);
    syncActorTransform(this.actor.id, "position", this.positionValue);
  }

  get rotation(): Quaternion {
    return cloneQuaternion(this.rotationValue);
  }

  set rotation(value: Quaternion) {
    this.rotationValue = cloneQuaternion(value);
    syncActorTransform(this.actor.id, "rotation", this.rotationValue);
  }

  get localPosition(): Vector3 {
    return cloneVector3(this.localPositionValue);
  }

  set localPosition(value: Vector3) {
    this.localPositionValue = cloneVector3(value);
    syncActorTransform(this.actor.id, "localPosition", this.localPositionValue);
  }

  get localRotation(): Quaternion {
    return cloneQuaternion(this.localRotationValue);
  }

  set localRotation(value: Quaternion) {
    this.localRotationValue = cloneQuaternion(value);
    syncActorTransform(this.actor.id, "localRotation", this.localRotationValue);
  }

  get parent(): number | null {
    return this.parentValue;
  }

  set parent(value: { readonly id: number } | number | null) {
    const parent = isActorLike(value) ? value.id : value;
    this.parentValue = parent;
    syncActorParent(this.actor.id, parent);
  }
}

interface TransformState {
  readonly position: Vector3;
  readonly rotation: Quaternion;
  readonly localPosition: Vector3;
  readonly localRotation: Quaternion;
  readonly parent: number | null;
}

function isActorLike(value: unknown): value is { readonly id: number } {
  return value !== null &&
    typeof value === "object" &&
    "id" in value &&
    typeof (value as { readonly id?: unknown }).id === "number";
}

function zeroVector3(): Vector3 {
  return { x: 0, y: 0, z: 0 };
}

function identityQuaternion(): Quaternion {
  return { x: 0, y: 0, z: 0, w: 1 };
}

function cloneVector3(value: Vector3): Vector3 {
  return Object.freeze({ x: value.x, y: value.y, z: value.z });
}

function cloneQuaternion(value: Quaternion): Quaternion {
  return Object.freeze({ x: value.x, y: value.y, z: value.z, w: value.w });
}
