import { Component } from "./component.js";
import type { ComponentConstructor, ComponentType } from "./types.js";

export class ComponentRegistry {
  private static readonly constructors =
    new Map<ComponentType, ComponentConstructor<Component>>();

  private constructor() {}

  static register<TComponent extends Component>(
    constructor: ComponentConstructor<TComponent>,
  ): void {
    if (constructor.componentType <= 0) {
      throw new Error("Component type must be a positive integer.");
    }
    if (this.constructors.has(constructor.componentType)) {
      throw new Error(`Component already registered: ${constructor.componentType}`);
    }
    this.constructors.set(
      constructor.componentType,
      constructor as ComponentConstructor<Component>);
  }

  static has(type: ComponentType): boolean {
    return this.constructors.has(type);
  }

  static require<TConstructor extends ComponentConstructor>(
    constructor: TConstructor,
  ): TConstructor {
    if (!this.has(constructor.componentType)) {
      this.register(constructor);
    }
    return constructor;
  }

  static reset(): void {
    this.constructors.clear();
  }
}
