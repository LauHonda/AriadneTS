import type {
  ActorFactoryContext,
  ActorLifecycle,
  ActorSnapshot,
  ActorTickPayload,
  ActorType,
  Quaternion,
  Vector3,
} from "./types.js";
import { Component } from "../components/component.js";
import { ComponentRegistry } from "../components/registry.js";
import { TransformComponent } from "../components/transform-component.js";
import { createEngineComponent, destroyEngineComponent } from "../components/bridge.js";
import type { ComponentConstructor, ComponentSnapshot } from "../components/types.js";

export abstract class Actor implements ActorLifecycle {
  readonly id: number;
  readonly type: ActorType;
  readonly name: string;
  readonly props?: unknown;

  private activeValue = true;
  private begunValue = false;
  private destroyedValue = false;
  private readonly componentsById = new Map<number, Component>();
  private readonly componentsByType = new Map<number, Component[]>();
  private nextComponentId = 1;
  private engineActorCreated = false;

  protected constructor(context: ActorFactoryContext) {
    this.id = context.id;
    this.type = context.type;
    this.name = context.name;
    this.props = context.props;
    this.addComponent(TransformComponent, {
      position: context.position,
      rotation: context.rotation,
      localPosition: context.localPosition,
      localRotation: context.localRotation,
      parent: context.parent,
    });
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

  get destroyed(): boolean {
    return this.destroyedValue;
  }

  get transform(): TransformComponent {
    return this.getOrAddComponent(TransformComponent);
  }

  get position(): Vector3 {
    return this.transform.position;
  }

  set position(value: Vector3) {
    this.transform.position = value;
  }

  get rotation(): Quaternion {
    return this.transform.rotation;
  }

  set rotation(value: Quaternion) {
    this.transform.rotation = value;
  }

  get localPosition(): Vector3 {
    return this.transform.localPosition;
  }

  set localPosition(value: Vector3) {
    this.transform.localPosition = value;
  }

  get localRotation(): Quaternion {
    return this.transform.localRotation;
  }

  set localRotation(value: Quaternion) {
    this.transform.localRotation = value;
  }

  get parent(): number | null {
    return this.transform.parent;
  }

  set parent(value: Actor | number | null) {
    this.transform.parent = value;
  }

  addComponent<TConstructor extends ComponentConstructor>(
    constructor: TConstructor,
    props?: unknown,
  ): InstanceType<TConstructor> {
    ComponentRegistry.require(constructor);
    const id = this.nextComponentId++;
    const component = new constructor({
      id,
      type: constructor.componentType,
      actor: this,
      props,
    });
    this.componentsById.set(component.id, component);
    const components = this.componentsByType.get(component.type) ?? [];
    components.push(component);
    this.componentsByType.set(component.type, components);
    if (this.engineActorCreated && component.type !== TransformComponent.componentType) {
      createEngineComponent({
        actorId: this.id,
        componentId: component.id,
        type: component.type,
      });
    }
    component.onAttach();
    if (this.begun && component.active) {
      component.markBegun();
      component.onBeginPlay();
    }
    return component as InstanceType<TConstructor>;
  }

  getComponent<TConstructor extends ComponentConstructor>(
    constructor: TConstructor,
  ): InstanceType<TConstructor> | undefined {
    const components = this.componentsByType.get(constructor.componentType);
    return components?.[0] as InstanceType<TConstructor> | undefined;
  }

  getComponents<TConstructor extends ComponentConstructor>(
    constructor: TConstructor,
  ): readonly InstanceType<TConstructor>[] {
    return Object.freeze([
      ...(this.componentsByType.get(constructor.componentType) ?? []),
    ]) as readonly InstanceType<TConstructor>[];
  }

  getOrAddComponent<TConstructor extends ComponentConstructor>(
    constructor: TConstructor,
    props?: unknown,
  ): InstanceType<TConstructor> {
    return this.getComponent(constructor) ?? this.addComponent(constructor, props);
  }

  hasComponent<TConstructor extends ComponentConstructor>(
    constructor: TConstructor,
  ): boolean {
    return this.componentsByType.has(constructor.componentType);
  }

  removeComponent(componentOrId: Component | number): boolean {
    const id = typeof componentOrId === "number" ? componentOrId : componentOrId.id;
    const component = this.componentsById.get(id);
    if (!component) {
      return false;
    }

    if (component.begun) {
      component.onEndPlay();
      component.markEnded();
    }
    component.onDetach();
    if (this.engineActorCreated && component.type !== TransformComponent.componentType) {
      destroyEngineComponent(component.id);
    }
    component.markDetached();
    this.componentsById.delete(component.id);

    const components = this.componentsByType.get(component.type);
    if (components) {
      const index = components.indexOf(component);
      if (index >= 0) {
        components.splice(index, 1);
      }
      if (components.length === 0) {
        this.componentsByType.delete(component.type);
      }
    }
    return true;
  }

  onCreate(): void {}

  onBeginPlay(): void {}

  onTick(_payload: ActorTickPayload): void {}

  onEndPlay(): void {}

  onDestroy(): void {}

  snapshot(): ActorSnapshot {
    return Object.freeze({
      id: this.id,
      type: this.type,
      name: this.name,
      active: this.active,
      begun: this.begun,
      destroyed: this.destroyed,
      position: this.position,
      rotation: this.rotation,
      localPosition: this.localPosition,
      localRotation: this.localRotation,
      parent: this.parent,
    });
  }

  componentSnapshots(): readonly ComponentSnapshot[] {
    return Object.freeze([...this.componentsById.values()].map((component) =>
      component.snapshot()));
  }

  markBegun(): void {
    this.begunValue = true;
  }

  beginComponents(): void {
    for (const component of this.componentsById.values()) {
      if (component.active && !component.begun) {
        component.markBegun();
        component.onBeginPlay();
      }
    }
  }

  markEnded(): void {
    for (const component of this.componentsById.values()) {
      if (component.begun) {
        component.onEndPlay();
        component.markEnded();
      }
    }
    this.begunValue = false;
  }

  markDestroyed(): void {
    for (const component of [...this.componentsById.values()]) {
      this.removeComponent(component);
    }
    this.destroyedValue = true;
    this.activeValue = false;
  }

  tickComponents(payload: ActorTickPayload): void {
    for (const component of this.componentsById.values()) {
      if (component.active && component.begun && !component.detached) {
        component.onTick(payload);
      }
    }
  }

  markEngineActorCreated(): void {
    this.engineActorCreated = true;
    for (const component of this.componentsById.values()) {
      if (component.type !== TransformComponent.componentType) {
        createEngineComponent({
          actorId: this.id,
          componentId: component.id,
          type: component.type,
        });
      }
    }
  }

}
