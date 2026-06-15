import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const toolsDirectory = path.dirname(fileURLToPath(import.meta.url));
const rootDirectory = path.resolve(toolsDirectory, "..");
const distDirectory = path.join(rootDirectory, "TypeScript", "dist");
const manifestBytes = await readFile(path.join(distDirectory, "manifest.json"));
const manifest = JSON.parse(manifestBytes.toString("utf8"));
const outputDirectory = path.join(rootDirectory, "Build", "script-packages", manifest.Version);
const signatureBytes = await readFile(path.join(distDirectory, "manifest.sig"));

await rm(outputDirectory, { recursive: true, force: true });
await mkdir(outputDirectory, { recursive: true });

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

const packagePath = path.join(outputDirectory, "typescript-package.bytes");
await writeFile(packagePath, Buffer.concat(chunks));
console.log(`Packaged signed script update at ${packagePath}`);

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
