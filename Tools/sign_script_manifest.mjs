import { readFile, writeFile } from "node:fs/promises";
import { createPublicKey, sign } from "node:crypto";
import path from "node:path";
import { fileURLToPath } from "node:url";

const toolsDirectory = path.dirname(fileURLToPath(import.meta.url));
const rootDirectory = path.resolve(toolsDirectory, "..");
const distDirectory = path.join(rootDirectory, "TypeScript", "dist");
const privateKeyPath = process.env.SCRIPT_PACKAGE_PRIVATE_KEY;

if (!privateKeyPath) {
  throw new Error("SCRIPT_PACKAGE_PRIVATE_KEY must point to an RSA private key PEM file.");
}

const manifestPath = path.join(distDirectory, "manifest.json");
const manifestBytes = await readFile(manifestPath);
const privateKey = await readFile(privateKeyPath, "utf8");
const signature = sign("RSA-SHA256", manifestBytes, privateKey);
const publicKeyJwk = createPublicKey(privateKey).export({ format: "jwk" });

await writeFile(path.join(distDirectory, "manifest.sig"), signature);
await writeFile(
  path.join(distDirectory, "public-key.txt"),
  `RSA1.${publicKeyJwk.n}.${publicKeyJwk.e}\n`,
  "utf8",
);

console.log("Signed script package manifest");
