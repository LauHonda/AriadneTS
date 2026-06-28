import { createHash } from "node:crypto";
import { readdir, readFile, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const toolsDirectory = path.dirname(fileURLToPath(import.meta.url));
const rootDirectory = path.resolve(toolsDirectory, "..");
const distDirectory = path.join(rootDirectory, "TypeScript", "dist");
const version = process.argv[2] ?? "local-dev";
const buildNumber = Number.parseInt(process.env.SCRIPT_PACKAGE_BUILD_NUMBER ?? "0", 10);
const requiredRuntimeAbiVersion = 5;
const entryModule = "bootstrap.js";

if (!Number.isSafeInteger(buildNumber) || buildNumber < 0) {
  throw new Error("SCRIPT_PACKAGE_BUILD_NUMBER must be a non-negative integer");
}

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

await writeFile(
  path.join(distDirectory, "manifest.json"),
  `${JSON.stringify(manifest, null, 2)}\n`,
  "utf8",
);

console.log(`Generated script package manifest for ${version}`);

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
