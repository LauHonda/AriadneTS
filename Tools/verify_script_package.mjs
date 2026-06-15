import { createHash, createPublicKey, verify } from "node:crypto";
import { readFile } from "node:fs/promises";
import path from "node:path";

if (process.argv.length !== 4) {
  throw new Error("Usage: node verify_script_package.mjs <package.bytes> <public-key.txt>");
}

const packageBytes = await readFile(path.resolve(process.argv[2]));
const publicKeyText = (await readFile(process.argv[3], "utf8")).trim();
const publicKeyParts = publicKeyText.split(".");
if (publicKeyParts.length !== 3 || publicKeyParts[0] !== "RSA1") {
  throw new Error("Public key must use RSA1.<modulus>.<exponent> format");
}
const publicKey = createPublicKey({
  key: {
    kty: "RSA",
    n: publicKeyParts[1],
    e: publicKeyParts[2],
  },
  format: "jwk",
});
let offset = 0;
requireBytes(read(8), Buffer.from("ARDPKG01", "ascii"), "magic");
requireValue(readUInt32(), 1, "format version");
const manifestLength = readUInt32();
const signatureLength = readUInt32();
const fileCount = readUInt32();
const manifestBytes = read(manifestLength);
const signatureBytes = read(signatureLength);

if (!verify("RSA-SHA256", manifestBytes, publicKey, signatureBytes)) {
  throw new Error("Script package manifest signature is invalid");
}

const manifest = JSON.parse(manifestBytes.toString("utf8"));
requireValue(fileCount, manifest.Files.length, "file count");
for (const file of manifest.Files) {
  const pathLength = readUInt32();
  const dataLength = Number(readUInt64());
  const filePath = read(pathLength).toString("utf8");
  requireValue(filePath, file.Path, "file path");
  requireValue(dataLength, file.SizeBytes, "file size");
  const hash = createHash("sha256").update(read(dataLength)).digest("hex");
  if (hash !== file.Sha256.toLowerCase()) {
    throw new Error(`Script package hash mismatch: ${file.Path}`);
  }
}
requireValue(offset, packageBytes.length, "package length");

console.log(`Verified signed script package ${manifest.Version}`);

function read(length) {
  if (offset + length > packageBytes.length) {
    throw new Error("Script package is truncated");
  }
  const value = packageBytes.subarray(offset, offset + length);
  offset += length;
  return value;
}

function readUInt32() {
  return read(4).readUInt32LE();
}

function readUInt64() {
  return read(8).readBigUInt64LE();
}

function requireBytes(actual, expected, field) {
  if (!actual.equals(expected)) {
    throw new Error(`Invalid script package ${field}`);
  }
}

function requireValue(actual, expected, field) {
  if (actual !== expected) {
    throw new Error(`Invalid script package ${field}: expected ${expected}, got ${actual}`);
  }
}
