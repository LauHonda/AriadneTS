import { invokeHost } from "./host-api.js";

export interface ReloadState {
  elapsedSeconds: number;
}

export interface DeltaTimePayload {
  deltaTime: number;
}

export class GameApplication {
  private elapsedSeconds = 0;

  start(): void {
    host.log("TypeScript application started");
    const player = invokeHost<{ name: string; engine: string }>(
      "demo.getPlayer",
      { requestedBy: "TypeScript" },
    );
    host.log(`TypeScript received C# result: ${player.name} on ${player.engine}`);
    Promise.resolve().then(() => host.log("TypeScript promise job ran"));
  }

  update(payload: DeltaTimePayload): void {
    this.elapsedSeconds += payload.deltaTime;
  }

  lateUpdate(_payload: DeltaTimePayload): void {
    // Reserved for business systems that require a late-frame phase.
  }

  beforeReload(): ReloadState {
    return { elapsedSeconds: this.elapsedSeconds };
  }

  afterReload(state: ReloadState | null): void {
    this.elapsedSeconds = state?.elapsedSeconds ?? 0;
  }

  shutdown(): void {
    host.log("TypeScript application stopped");
  }

  greet(payload: { message: string }): { reply: string } {
    host.log(`TypeScript received C# call: ${payload.message}`);
    return { reply: `Hello from TypeScript, ${payload.message}` };
  }
}
