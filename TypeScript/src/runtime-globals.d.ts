interface RuntimeHost {
  log(message: unknown): void;
}

declare const host: RuntimeHost;

declare var __ariadnets_invoke:
  | ((method: string, payload: unknown) => unknown)
  | undefined;

