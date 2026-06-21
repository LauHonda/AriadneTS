import { mkdir, readFile, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import path from "node:path";

const args = parseArgs(process.argv.slice(2));
const launchJsonPath = requireText(args["launch-json"], "--launch-json");
const host = args.host || "127.0.0.1";
const port = clampPort(Number.parseInt(args.port ?? "9229", 10));
const tsRoot = args["ts-root"] || "${workspaceFolder}/TypeScript";
const pollIntervalMs = Math.max(50, Number.parseInt(args["poll-interval-ms"] ?? "250", 10));

const config = {
  type: "ariadnets",
  request: "attach",
  name: "Attach AriadneTS",
  host,
  port,
  tsRoot,
  pollIntervalMs,
};

await mkdir(path.dirname(launchJsonPath), { recursive: true });

let launch = {
  version: "0.2.0",
  configurations: [],
};

if (existsSync(launchJsonPath)) {
  const existing = stripBom(await readFile(launchJsonPath, "utf8"));
  if (existing.trim().length > 0) {
    try {
      launch = JSON.parse(existing);
    } catch (error) {
      throw new Error(
        `Could not parse existing VSCode launch.json at ${launchJsonPath}. ` +
        `Remove comments or fix JSON syntax before AriadneTS can update it. ${error instanceof Error ? error.message : String(error)}`,
      );
    }
  }
}

if (!launch || typeof launch !== "object" || Array.isArray(launch)) {
  launch = {};
}
launch.version = typeof launch.version === "string" ? launch.version : "0.2.0";
launch.configurations = Array.isArray(launch.configurations) ? launch.configurations : [];

const existingIndex = launch.configurations.findIndex((item) =>
  item && typeof item === "object" && item.type === "ariadnets",
);
if (existingIndex >= 0) {
  launch.configurations[existingIndex] = {
    ...launch.configurations[existingIndex],
    ...config,
  };
} else {
  launch.configurations.push(config);
}

await writeFile(launchJsonPath, `${JSON.stringify(launch, null, 2)}\n`, "utf8");
console.log(`Updated AriadneTS VSCode launch config: ${launchJsonPath}`);

function parseArgs(argv) {
  const parsed = {};
  for (let index = 0; index < argv.length; ++index) {
    const key = argv[index];
    if (!key.startsWith("--")) {
      continue;
    }
    parsed[key.slice(2)] = argv[index + 1] ?? "";
    ++index;
  }
  return parsed;
}

function requireText(value, name) {
  if (!value) {
    throw new Error(`${name} is required`);
  }
  return value;
}

function stripBom(text) {
  return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
}

function clampPort(value) {
  if (!Number.isFinite(value)) {
    return 9229;
  }
  return Math.max(1, Math.min(65535, value));
}
