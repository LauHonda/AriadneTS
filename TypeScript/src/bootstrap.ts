import {
  handleAsyncCompletion,
  handleAsyncProgress,
  type AsyncBridgeCompletion,
  type AsyncBridgeProgress,
} from "../ariadnets-sdk/index.js";
import {
  type DeltaTimePayload,
  GameApplication,
  type ReloadState,
} from "./game-application.js";

const application = new GameApplication();

globalThis.__ariadnets_invoke = (method: string, payload: unknown): unknown => {
  switch (method) {
    case "onBeginPlay":
    case "start":
      return application.onBeginPlay();
    case "onTick":
    case "update":
      return application.onTick(payload as DeltaTimePayload);
    case "onEndPlay":
    case "shutdown":
      return application.onEndPlay();
    case "ariadnets.async.progress":
      return handleAsyncProgress(payload as AsyncBridgeProgress);
    case "ariadnets.async.complete":
      return handleAsyncCompletion(payload as AsyncBridgeCompletion);
    case "beforeReload":
      return application.beforeReload();
    case "afterReload":
      return application.afterReload(payload as ReloadState | null);
    case "demo.greet":
      return application.greet(payload as { message: string });
    default:
      throw new Error(`Unknown lifecycle method: ${method}`);
  }
};
