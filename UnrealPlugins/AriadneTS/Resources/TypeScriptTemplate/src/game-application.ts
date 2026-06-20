import {
  Actor,
  Ariadne,
  TransformComponent,
  type ActorFactoryContext,
} from "../ariadnets-sdk/index.js";
import { invokeHost } from "./host-api.js";

export interface ReloadState {
  elapsedSeconds: number;
}

export interface DeltaTimePayload {
  deltaTime: number;
}

export enum DemoActorType {
  Player = 1,
}

class PlayerActor extends Actor {
  private elapsedSeconds = 0;

  constructor(context: ActorFactoryContext) {
    super(context);
  }

  override onCreate(): void {
    Ariadne.log(`Actor created: ${this.name}`);
  }

  override onBeginPlay(): void {
    Ariadne.log(`Actor begin play: ${this.name}`);
  }

  override onTick(payload: DeltaTimePayload): void {
    this.elapsedSeconds += payload.deltaTime;
  }

  override onEndPlay(): void {
    Ariadne.log(`Actor end play: ${this.name}, elapsed=${this.elapsedSeconds.toFixed(2)}s`);
  }

  override onDestroy(): void {
    Ariadne.log(`Actor destroyed: ${this.name}`);
  }
}

export class GameApplication {
  private elapsedSeconds = 0;

  constructor() {
    if (!Ariadne.actors.hasFactory(DemoActorType.Player)) {
      Ariadne.actors.register(
        DemoActorType.Player,
        (context) => new PlayerActor(context),
      );
    }
  }

  onBeginPlay(): void {
    Ariadne.log("TypeScript application started");
    const player = invokeHost<{ name: string; engine: string }>(
      "demo.getPlayer",
      { requestedBy: "TypeScript" },
    );
    Ariadne.log(`TypeScript received C# result: ${player.name} on ${player.engine}`);
    Ariadne.warning("This is a warning log from the AriadneTS API skeleton");
    Ariadne.assets.verifyAsync({ groups: ["startup"] }, {
      onError(reason: unknown): void {
        Ariadne.warning(`Asset bridge is not implemented yet: ${String(reason)}`);
      },
    });
    const demoPlayer = Ariadne.actors.create({
      type: DemoActorType.Player,
      name: "DemoPlayer",
      position: { x: 1, y: 0, z: 0 },
      props: { spawnReason: "bootstrap" },
    });
    const transform = demoPlayer.getOrAddComponent(TransformComponent);
    transform.localPosition = { x: 2, y: 0, z: 0 };
    Ariadne.actors.beginPlay();
    Promise.resolve().then(() => Ariadne.log("TypeScript promise job ran"));
  }

  onTick(payload: DeltaTimePayload): void {
    this.elapsedSeconds += payload.deltaTime;
    Ariadne.actors.tick(payload);
  }

  beforeReload(): ReloadState {
    return { elapsedSeconds: this.elapsedSeconds };
  }

  afterReload(state: ReloadState | null): void {
    this.elapsedSeconds = state?.elapsedSeconds ?? 0;
  }

  onEndPlay(): void {
    Ariadne.actors.endPlay();
    Ariadne.actors.destroyAll();
    Ariadne.log("TypeScript application stopped");
  }

  greet(payload: { message: string }): { reply: string } {
    Ariadne.log(`TypeScript received C# call: ${payload.message}`);
    if (!payload.message) {
      Ariadne.error("Greeting payload message is empty");
    }
    return { reply: `Hello from TypeScript, ${payload.message}` };
  }
}
