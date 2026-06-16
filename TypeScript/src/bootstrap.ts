import {
  type DeltaTimePayload,
  GameApplication,
  type ReloadState,
} from "./game-application.js";

const application = new GameApplication();

globalThis.__ariadnets_invoke = (method: string, payload: unknown): unknown => {
  switch (method) {
    case "start":
      return application.start();
    case "update":
      return application.update(payload as DeltaTimePayload);
    case "lateUpdate":
      return application.lateUpdate(payload as DeltaTimePayload);
    case "beforeReload":
      return application.beforeReload();
    case "afterReload":
      return application.afterReload(payload as ReloadState | null);
    case "shutdown":
      return application.shutdown();
    case "demo.greet":
      return application.greet(payload as { message: string });
    default:
      throw new Error(`Unknown lifecycle method: ${method}`);
  }
};
