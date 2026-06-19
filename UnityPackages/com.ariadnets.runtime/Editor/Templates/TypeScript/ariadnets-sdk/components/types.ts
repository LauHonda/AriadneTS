import type { Actor } from "../actors/actor.js";
import type { Component } from "./component.js";

export type ComponentType = number;

export interface ComponentTickPayload {
  readonly deltaTime: number;
}

export interface ComponentCreateContext {
  readonly id: number;
  readonly type: ComponentType;
  readonly actor: Actor;
  readonly props?: unknown;
}

export interface ComponentLifecycle {
  onAttach?(): void;
  onBeginPlay?(): void;
  onTick?(payload: ComponentTickPayload): void;
  onEndPlay?(): void;
  onDetach?(): void;
}

export type ComponentConstructor<TComponent extends Component = Component> = {
  readonly componentType: ComponentType;
  new(context: ComponentCreateContext): TComponent;
};

export interface ComponentSnapshot {
  readonly id: number;
  readonly type: ComponentType;
  readonly actorId: number;
  readonly active: boolean;
  readonly begun: boolean;
  readonly detached: boolean;
}
