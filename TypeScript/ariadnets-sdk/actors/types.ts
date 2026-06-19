export type ActorType = number;

export interface ActorTickPayload {
  readonly deltaTime: number;
}

export interface Vector3 {
  readonly x: number;
  readonly y: number;
  readonly z: number;
}

export interface Quaternion {
  readonly x: number;
  readonly y: number;
  readonly z: number;
  readonly w: number;
}

export interface ActorCreateContext {
  readonly type: ActorType;
  readonly name?: string;
  readonly props?: unknown;
  readonly position?: Vector3;
  readonly rotation?: Quaternion;
  readonly localPosition?: Vector3;
  readonly localRotation?: Quaternion;
  readonly parent?: number | null;
}

export interface ActorFactoryContext {
  readonly id: number;
  readonly type: ActorType;
  readonly name: string;
  readonly props?: unknown;
  readonly position?: Vector3;
  readonly rotation?: Quaternion;
  readonly localPosition?: Vector3;
  readonly localRotation?: Quaternion;
  readonly parent?: number | null;
}

export interface ActorLifecycle {
  onCreate?(): void;
  onBeginPlay?(): void;
  onTick?(payload: ActorTickPayload): void;
  onEndPlay?(): void;
  onDestroy?(): void;
}

export type ActorFactory<TActor> = (context: ActorFactoryContext) => TActor;

export interface ActorSnapshot {
  readonly id: number;
  readonly type: ActorType;
  readonly name: string;
  readonly active: boolean;
  readonly begun: boolean;
  readonly destroyed: boolean;
  readonly position: Vector3;
  readonly rotation: Quaternion;
  readonly localPosition: Vector3;
  readonly localRotation: Quaternion;
  readonly parent: number | null;
}
