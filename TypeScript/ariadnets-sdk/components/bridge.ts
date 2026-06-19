import { callSync } from "../core/index.js";

export function createEngineComponent(params: {
  readonly actorId: number;
  readonly componentId: number;
  readonly type: number;
}): void {
  callSync("components.add", params);
}

export function destroyEngineComponent(componentId: number): void {
  callSync("components.remove", { componentId });
}

export function syncComponentProperty(
  componentId: number,
  property: string,
  value: unknown,
): void {
  callSync("components.setProperty", { componentId, property, value });
}
