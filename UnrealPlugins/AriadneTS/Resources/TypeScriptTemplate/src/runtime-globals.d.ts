interface RuntimeHost {
  log(message: unknown): void;
  invoke(method: string, payload?: unknown): unknown;
}

declare const host: RuntimeHost;

declare var __ariadnets_invoke:
  | ((method: string, payload: unknown) => unknown)
  | undefined;
