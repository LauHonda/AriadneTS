import type { Actor } from "../actors/actor.js";
import type {
  ComponentCreateContext,
  ComponentLifecycle,
  ComponentSnapshot,
  ComponentTickPayload,
  ComponentType,
} from "./types.js";

export abstract class Component implements ComponentLifecycle {
  static readonly componentType: ComponentType = 0;

  readonly id: number;
  readonly type: ComponentType;
  readonly actor: Actor;
  readonly props?: unknown;

  private activeValue = true;
  private begunValue = false;
  private detachedValue = false;

  protected constructor(context: ComponentCreateContext) {
    this.id = context.id;
    this.type = context.type;
    this.actor = context.actor;
    this.props = context.props;
  }

  get active(): boolean {
    return this.activeValue;
  }

  set active(value: boolean) {
    this.activeValue = value;
  }

  get begun(): boolean {
    return this.begunValue;
  }

  get detached(): boolean {
    return this.detachedValue;
  }

  onAttach(): void {}

  onBeginPlay(): void {}

  onTick(_payload: ComponentTickPayload): void {}

  onEndPlay(): void {}

  onDetach(): void {}

  snapshot(): ComponentSnapshot {
    return Object.freeze({
      id: this.id,
      type: this.type,
      actorId: this.actor.id,
      active: this.active,
      begun: this.begun,
      detached: this.detached,
    });
  }

  markBegun(): void {
    this.begunValue = true;
  }

  markEnded(): void {
    this.begunValue = false;
  }

  markDetached(): void {
    this.detachedValue = true;
    this.activeValue = false;
  }
}
