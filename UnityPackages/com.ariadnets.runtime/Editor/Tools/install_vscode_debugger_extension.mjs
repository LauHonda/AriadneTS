import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const toolsDirectory = path.dirname(fileURLToPath(import.meta.url));
const sourceDirectory = path.join(toolsDirectory, "vscode-ariadnets-debugger");
const extensionDirectory = resolveExtensionDirectory();
const destinationDirectory = path.join(extensionDirectory, "ariadnets.ariadnets-debugger-0.1.0");

if (!existsSync(path.join(sourceDirectory, "package.json"))) {
  throw new Error(`AriadneTS VSCode debugger extension is missing: ${sourceDirectory}`);
}

await mkdir(extensionDirectory, { recursive: true });
await rm(destinationDirectory, { recursive: true, force: true });
await cp(sourceDirectory, destinationDirectory, {
  recursive: true,
  filter: (source) => !source.endsWith(".meta"),
});
await removeObsoleteMarker(extensionDirectory, path.basename(destinationDirectory));
await updateExtensionsIndex(extensionDirectory, destinationDirectory);

console.log(`Installed AriadneTS VSCode debugger extension to: ${destinationDirectory}`);
console.log("Restart VSCode completely, or run Developer: Reload Window.");
console.log("In VSCode Extensions, search @installed AriadneTS Debugger to verify it is loaded.");

function resolveExtensionDirectory() {
  if (process.env.VSCODE_EXTENSIONS) {
    return process.env.VSCODE_EXTENSIONS;
  }
  return path.join(os.homedir(), ".vscode", "extensions");
}

async function removeObsoleteMarker(extensionDirectory, extensionFolderName) {
  const obsoletePath = path.join(extensionDirectory, ".obsolete");
  if (!existsSync(obsoletePath)) {
    return;
  }

  let obsolete;
  try {
    obsolete = JSON.parse(await readFile(obsoletePath, "utf8"));
  } catch {
    return;
  }

  if (!obsolete || typeof obsolete !== "object" || !(extensionFolderName in obsolete)) {
    return;
  }

  delete obsolete[extensionFolderName];
  await writeFile(obsoletePath, `${JSON.stringify(obsolete)}\n`, "utf8");
}

async function updateExtensionsIndex(extensionDirectory, destinationDirectory) {
  const indexPath = path.join(extensionDirectory, "extensions.json");
  let extensions = [];
  if (existsSync(indexPath)) {
    try {
      const parsed = JSON.parse(await readFile(indexPath, "utf8"));
      if (Array.isArray(parsed)) {
        extensions = parsed;
      }
    } catch {
      extensions = [];
    }
  }

  const identifier = "ariadnets.ariadnets-debugger";
  extensions = extensions.filter((entry) => entry?.identifier?.id !== identifier);
  extensions.push({
    identifier: {
      id: identifier,
    },
    version: "0.1.0",
    location: {
      $mid: 1,
      path: destinationDirectory,
      scheme: "file",
    },
    relativeLocation: path.basename(destinationDirectory),
    metadata: {
      installedTimestamp: Date.now(),
      source: "local",
      publisherDisplayName: "ariadnets",
      targetPlatform: "undefined",
      isPreReleaseVersion: false,
      hasPreReleaseVersion: false,
    },
  });

  await writeFile(indexPath, `${JSON.stringify(extensions)}\n`, "utf8");
}
