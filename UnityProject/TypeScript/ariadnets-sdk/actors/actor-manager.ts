import { Actor } from "./actor.js";
import { createEngineActor, destroyEngineActor } from "./bridge.js";
import type {
  ActorCreateContext,
  ActorFactory,
  ActorSnapshot,
  ActorTickPayload,
  ActorType,
} from "./types.js";

export class ActorManager {
  private static readonly factories = new Map<ActorType, ActorFactory<Actor>>();
  private static readonly actors = new Map<number, Actor>();
  private static nextActorId = 1;
  private static begun = false;

  private constructor() {}

  static register<TActor extends Actor>(
    type: ActorType,
    factory: ActorFactory<TActor>,
  ): void {
    if (this.factories.has(type)) {
      throw new Error(`Actor factory already registered: ${type}`);
    }
    this.factories.set(type, factory);
  }

  static unregister(type: ActorType): void {
    this.factories.delete(type);
  }

  static hasFactory(type: ActorType): boolean {
    return this.factories.has(type);
  }

  static create<TActor extends Actor = Actor>(context: ActorCreateContext): TActor {
    const factory = this.factories.get(context.type);
    if (!factory) {
      throw new Error(`Actor factory is not registered: ${context.type}`);
    }

    const id = this.nextActorId++;
    const actor = factory({
      id,
      type: context.type,
      name: context.name ?? `Actor${context.type}#${id}`,
      props: context.props,
      position: context.position,
      rotation: context.rotation,
      localPosition: context.localPosition,
      localRotation: context.localRotation,
      parent: context.parent,
    }) as TActor;

    this.actors.set(actor.id, actor);
    createEngineActor({
      id: actor.id,
      type: actor.type,
      name: actor.name,
      position: actor.position,
      rotation: actor.rotation,
      localPosition: actor.localPosition,
      localRotation: actor.localRotation,
      parent: actor.parent,
    });
    actor.markEngineActorCreated();
    actor.onCreate();
    if (this.begun && actor.active) {
      actor.markBegun();
      actor.onBeginPlay();
      actor.beginComponents();
    }
    return actor;
  }

  static get<TActor extends Actor = Actor>(id: number): TActor | undefined {
    return this.actors.get(id) as TActor | undefined;
  }

  static require<TActor extends Actor = Actor>(id: number): TActor {
    const actor = this.get<TActor>(id);
    if (!actor) {
      throw new Error(`Actor does not exist: ${id}`);
    }
    return actor;
  }

  static destroy(actorOrId: Actor | number): boolean {
    const id = typeof actorOrId === "number" ? actorOrId : actorOrId.id;
    const actor = this.actors.get(id);
    if (!actor) {
      return false;
    }

    if (actor.begun) {
      actor.onEndPlay();
      actor.markEnded();
    }
    actor.onDestroy();
    actor.markDestroyed();
    destroyEngineActor(actor.id);
    this.actors.delete(id);
    return true;
  }

  static beginPlay(): void {
    if (this.begun) {
      return;
    }
    this.begun = true;
    for (const actor of this.actors.values()) {
      if (!actor.active || actor.begun) {
        continue;
      }
      actor.markBegun();
      actor.onBeginPlay();
      actor.beginComponents();
    }
  }

  static tick(payload: ActorTickPayload): void {
    if (!this.begun) {
      return;
    }
    for (const actor of this.actors.values()) {
      if (actor.active && actor.begun && !actor.destroyed) {
        actor.onTick(payload);
        actor.tickComponents(payload);
      }
    }
  }

  static endPlay(): void {
    if (!this.begun) {
      return;
    }
    for (const actor of this.actors.values()) {
      if (actor.begun) {
        actor.onEndPlay();
        actor.markEnded();
      }
    }
    this.begun = false;
  }

  static destroyAll(): void {
    for (const actor of [...this.actors.values()]) {
      this.destroy(actor);
    }
  }

  static snapshots(): readonly ActorSnapshot[] {
    return Object.freeze([...this.actors.values()].map((actor) => actor.snapshot()));
  }

  static reset(): void {
    this.destroyAll();
    this.factories.clear();
    this.nextActorId = 1;
    this.begun = false;
  }
}
