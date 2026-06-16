import { createHash, createPublicKey, generateKeyPairSync, sign } from "node:crypto";
import { chmod, mkdir, readdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";

const args = parseArgs(process.argv.slice(2));
const minimumTypeScriptVersion = { major: 5, minor: 0 };

if (args["generate-private-key"]) {
  await generatePrivateKey(args["private-key"]);
  process.exit(0);
}

if (args["print-public-key"]) {
  const privateKey = await readPrivateKey(args["private-key"]);
  const publicKey = createPublicKey(privateKey).export({ format: "jwk" });
  process.stdout.write(`RSA1.${publicKey.n}.${publicKey.e}\n`);
  process.exit(0);
}

const tsRoot = requirePath(args["ts-root"], "--ts-root");
const version = requireText(args.version, "--version");
const buildNumber = parseBuildNumber(args["build-number"]);
const privateKeyPath = requirePath(args["private-key"], "--private-key");
const outputPackagePath = requireText(args.output, "--output");
const requiredRuntimeAbiVersion = Number.parseInt(args["required-abi"] ?? "4", 10);
const entryModule = args.entry ?? "bootstrap.js";
const distDirectory = path.join(tsRoot, "dist");

const tsc = resolveTypeScriptCompiler(tsRoot);
run(tsc.command, [...tsc.args, "-p", "tsconfig.json"], tsRoot);

const manifestBytes = await createManifest(
  distDirectory,
  version,
  buildNumber,
  requiredRuntimeAbiVersion,
  entryModule,
);
const privateKey = await readPrivateKey(privateKeyPath);
const signature = sign("RSA-SHA256", manifestBytes, privateKey);
const publicKey = createPublicKey(privateKey).export({ format: "jwk" });
const publicKeyText = `RSA1.${publicKey.n}.${publicKey.e}`;

await writeFile(path.join(distDirectory, "manifest.sig"), signature);
await writeFile(path.join(distDirectory, "public-key.txt"), `${publicKeyText}\n`, "utf8");
await writePackage(distDirectory, outputPackagePath, manifestBytes, signature);
await writeFile(
  path.join(path.dirname(outputPackagePath), "public-key.txt"),
  `${publicKeyText}\n`,
  "utf8",
);

console.log(`Built AriadneTS package: ${outputPackagePath}`);
console.log(`Public key: ${publicKeyText}`);

function resolveTypeScriptCompiler(root) {
  const rejectedVersions = [];
  for (const candidate of localTypeScriptCompilerCandidates(root)) {
    if (existsSync(candidate) && isSupportedTypeScriptCompiler(process.execPath, [candidate], rejectedVersions)) {
      return { command: process.execPath, args: [candidate] };
    }
  }
  for (const candidate of globalTypeScriptCompilerCandidates()) {
    if (existsSync(candidate) && isSupportedTypeScriptCompiler(process.execPath, [candidate], rejectedVersions)) {
      return { command: process.execPath, args: [candidate] };
    }
  }
  if (isSupportedTypeScriptCompiler("tsc", [], rejectedVersions)) {
    return { command: "tsc", args: [] };
  }

  const versionNote = rejectedVersions.length > 0
    ? ` Found unsupported TypeScript compiler(s): ${rejectedVersions.join(", ")}.`
    : "";
  throw new Error(
    `TypeScript compiler 5.0 or newer was not found. Run npm install in ${root}, or install TypeScript globally.${versionNote} Unity may not inherit the shell PATH, so a terminal-only tsc command is not always visible to the Editor.`,
  );
}

function localTypeScriptCompilerCandidates(root) {
  return [
    path.join(root, "node_modules", "typescript", "bin", "tsc"),
    path.join(root, "node_modules", "typescript", "lib", "tsc.js"),
  ];
}

function globalTypeScriptCompilerCandidates() {
  const nodePrefix = path.dirname(path.dirname(process.execPath));
  const candidates = [
    path.join(nodePrefix, "lib", "node_modules", "typescript", "bin", "tsc"),
    path.join(nodePrefix, "lib", "node_modules", "typescript", "lib", "tsc.js"),
    "/opt/homebrew/lib/node_modules/typescript/bin/tsc",
    "/opt/homebrew/lib/node_modules/typescript/lib/tsc.js",
    "/usr/local/lib/node_modules/typescript/bin/tsc",
    "/usr/local/lib/node_modules/typescript/lib/tsc.js",
  ];
  if (process.platform === "win32") {
    const appData = process.env.APPDATA;
    if (appData) {
      candidates.push(
        path.join(appData, "npm", "node_modules", "typescript", "bin", "tsc"),
        path.join(appData, "npm", "node_modules", "typescript", "lib", "tsc.js"),
      );
    }
  }
  return candidates;
}

function isSupportedTypeScriptCompiler(command, commandArgs, rejectedVersions) {
  const result = spawnSync(command, [...commandArgs, "--version"], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
  });
  if (result.status !== 0) {
    return false;
  }

  const output = `${result.stdout}\n${result.stderr}`;
  const version = parseTypeScriptVersion(output);
  if (!version) {
    return false;
  }
  if (isMinimumTypeScriptVersion(version)) {
    return true;
  }

  rejectedVersions.push(`${command} ${commandArgs.join(" ")} (${version.text})`.trim());
  return false;
}

function parseTypeScriptVersion(output) {
  const match = output.match(/Version\s+(\d+)\.(\d+)\.(\d+)/i);
  if (!match) {
    return null;
  }
  return {
    major: Number.parseInt(match[1], 10),
    minor: Number.parseInt(match[2], 10),
    patch: Number.parseInt(match[3], 10),
    text: match[0],
  };
}

function isMinimumTypeScriptVersion(version) {
  if (version.major !== minimumTypeScriptVersion.major) {
    return version.major > minimumTypeScriptVersion.major;
  }
  return version.minor >= minimumTypeScriptVersion.minor;
}

async function createManifest(
  distDirectory,
  version,
  buildNumber,
  requiredRuntimeAbiVersion,
  entryModule,
) {
  const files = [];
  for (const relativePath of await listRuntimeFiles(distDirectory)) {
    const fullPath = path.join(distDirectory, relativePath);
    const contents = await readFile(fullPath);
    const info = await stat(fullPath);
    files.push({
      Path: relativePath.replaceAll(path.sep, "/"),
      SizeBytes: info.size,
      Sha256: createHash("sha256").update(contents).digest("hex"),
    });
  }

  if (!files.some((file) => file.Path === entryModule)) {
    throw new Error(`Entry module was not generated: ${entryModule}`);
  }

  const manifest = {
    Version: version,
    BuildNumber: buildNumber,
    RequiredRuntimeAbiVersion: requiredRuntimeAbiVersion,
    EntryModule: entryModule,
    Files: files,
  };
  const manifestBytes = Buffer.from(`${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  await writeFile(path.join(distDirectory, "manifest.json"), manifestBytes);
  return manifestBytes;
}

async function writePackage(distDirectory, packagePath, manifestBytes, signatureBytes) {
  const manifest = JSON.parse(manifestBytes.toString("utf8"));
  await mkdir(path.dirname(packagePath), { recursive: true });
  await rm(packagePath, { force: true });

  const chunks = [
    Buffer.from("ARDPKG01", "ascii"),
    uint32(1),
    uint32(manifestBytes.length),
    uint32(signatureBytes.length),
    uint32(manifest.Files.length),
    manifestBytes,
    signatureBytes,
  ];
  for (const file of manifest.Files) {
    const data = await readFile(safeJoin(distDirectory, file.Path));
    const filePath = Buffer.from(file.Path, "utf8");
    chunks.push(uint32(filePath.length), uint64(data.length), filePath, data);
  }
  await writeFile(packagePath, Buffer.concat(chunks));
}

async function listRuntimeFiles(directory, relativeDirectory = "") {
  const entries = await readdir(path.join(directory, relativeDirectory), { withFileTypes: true });
  const results = [];
  for (const entry of entries.sort((left, right) => left.name.localeCompare(right.name))) {
    const relativePath = path.join(relativeDirectory, entry.name);
    if (entry.isDirectory()) {
      results.push(...await listRuntimeFiles(directory, relativePath));
    } else if (entry.name.endsWith(".js") || entry.name.endsWith(".js.map")) {
      results.push(relativePath);
    }
  }
  return results;
}

async function readPrivateKey(privateKeyPath) {
  return await readFile(requireText(privateKeyPath, "--private-key"), "utf8");
}

async function generatePrivateKey(privateKeyPath) {
  const outputPath = requireText(privateKeyPath, "--private-key");
  if (existsSync(outputPath)) {
    throw new Error(`Private key already exists: ${outputPath}`);
  }

  const { privateKey } = generateKeyPairSync("rsa", {
    modulusLength: 3072,
    publicExponent: 0x10001,
  });
  const pem = privateKey.export({
    type: "pkcs8",
    format: "pem",
  });
  await mkdir(path.dirname(outputPath), { recursive: true });
  await writeFile(outputPath, pem, { encoding: "utf8", mode: 0o600 });
  if (process.platform !== "win32") {
    await chmod(outputPath, 0o600);
  }
  console.log(`Generated development private key: ${outputPath}`);
}

function run(command, commandArgs, cwd) {
  const result = spawnSync(command, commandArgs, {
    cwd,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
  });
  if (result.stdout) {
    process.stdout.write(result.stdout);
  }
  if (result.stderr) {
    process.stderr.write(result.stderr);
  }
  if (result.status !== 0) {
    throw new Error(`${command} ${commandArgs.join(" ")} failed with exit code ${result.status}`);
  }
}

function parseArgs(argv) {
  const parsed = {};
  for (let index = 0; index < argv.length; ++index) {
    const item = argv[index];
    if (!item.startsWith("--")) {
      throw new Error(`Unexpected argument: ${item}`);
    }
    const key = item.slice(2);
    if (key === "print-public-key") {
      parsed[key] = true;
    } else if (key === "generate-private-key") {
      parsed[key] = true;
    } else {
      parsed[key] = argv[++index];
    }
  }
  return parsed;
}

function parseBuildNumber(value) {
  const parsed = Number.parseInt(requireText(value, "--build-number"), 10);
  if (!Number.isSafeInteger(parsed) || parsed < 0) {
    throw new Error("--build-number must be a non-negative integer");
  }
  return parsed;
}

function requirePath(value, name) {
  const text = requireText(value, name);
  if (!existsSync(text)) {
    throw new Error(`${name} does not exist: ${text}`);
  }
  return text;
}

function requireText(value, name) {
  if (!value) {
    throw new Error(`${name} is required`);
  }
  return value;
}

function safeJoin(root, relativePath) {
  const resolvedRoot = path.resolve(root) + path.sep;
  const resolved = path.resolve(root, relativePath);
  if (!resolved.startsWith(resolvedRoot)) {
    throw new Error(`Package path escapes root: ${relativePath}`);
  }
  return resolved;
}

function uint32(value) {
  const buffer = Buffer.alloc(4);
  buffer.writeUInt32LE(value);
  return buffer;
}

function uint64(value) {
  const buffer = Buffer.alloc(8);
  buffer.writeBigUInt64LE(BigInt(value));
  return buffer;
}
