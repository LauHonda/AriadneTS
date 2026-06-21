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
const distDirectory = path.join(tsRoot, "dist");

await rm(distDirectory, { recursive: true, force: true });
const tsc = resolveTypeScriptCompiler(tsRoot);
run(tsc.command, [...tsc.args, "-p", "tsconfig.json"], tsRoot);
await instrumentDebugProbes(distDirectory);
await validateRuntimeJavaScript(distDirectory);
const entryModule = args.entry ?? resolveDefaultEntryModule(distDirectory);

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

function resolveDefaultEntryModule(distDirectory) {
  const candidates = [
    "bootstrap.js",
    "src/bootstrap.js",
  ];
  for (const candidate of candidates) {
    if (existsSync(path.join(distDirectory, candidate))) {
      return candidate;
    }
  }
  throw new Error(
    "Entry module was not generated. Expected dist/bootstrap.js or dist/src/bootstrap.js.",
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

async function instrumentDebugProbes(distDirectory) {
  for (const relativePath of await listRuntimeFiles(distDirectory)) {
    if (!relativePath.endsWith(".js")) {
      continue;
    }

    const fullPath = path.join(distDirectory, relativePath);
    const source = await readFile(fullPath, "utf8");
    const sourceMap = await readSourceMap(distDirectory, relativePath);
    const lines = source.split(/(?<=\n)/);
    const debugState = {
      blockStack: [],
      expressionDepth: 0,
    };
    const dynamicBreakpointsEnabled = shouldInstrumentDynamicBreakpoints(relativePath);
    let changed = false;
    const instrumented = lines.map((line, index) => {
      const generatedLine = index + 1;
      const generatedColumn = Math.max(0, line.search(/\S/));
      const position = resolveOriginalPosition(
        distDirectory,
        relativePath,
        sourceMap,
        generatedLine,
        generatedColumn,
      );
      const lineProbe = dynamicBreakpointsEnabled && shouldInstrumentDynamicBreakpointLine(line, debugState)
        ? `globalThis.__ariadnets_debug_line(${JSON.stringify(position.source)}, ${position.line}, ${position.column}, ${JSON.stringify(currentDebugFunctionName(debugState))}, ${createDebugVariablesCapture(debugState)}, (new Error()).stack);\n`
        : "";
      updateDebugState(debugState, line);
      if (!/\bdebugger\s*;/.test(line)) {
        if (!lineProbe) {
          return line;
        }
        changed = true;
        return lineProbe + line;
      }

      changed = true;
      const debuggerPosition = resolveOriginalPosition(
        distDirectory,
        relativePath,
        sourceMap,
        generatedLine,
        Math.max(0, line.indexOf("debugger")),
      );
      const checkpoint = `globalThis.__ariadnets_debug_checkpoint(${JSON.stringify(debuggerPosition.source)}, ${debuggerPosition.line}, ${debuggerPosition.column}, ${JSON.stringify(currentDebugFunctionName(debugState))}, ${createDebugVariablesCapture(debugState)}, (new Error()).stack);`;
      return lineProbe + line.replace(/\bdebugger\s*;/g, checkpoint);
    }).join("");

    if (changed) {
      await writeFile(fullPath, instrumented, "utf8");
    }
  }
}

function shouldInstrumentDynamicBreakpoints(relativePath) {
  const normalized = relativePath.replaceAll(path.sep, "/");
  return !normalized.startsWith("ariadnets-sdk/");
}

function shouldInstrumentDynamicBreakpointLine(line, debugState) {
  if (
    debugState.expressionDepth > 0 ||
    !debugState.blockStack.some((block) => block.executable)
  ) {
    return false;
  }

  const trimmed = line.trim();
  if (
    trimmed.length === 0 ||
    trimmed.startsWith("//") ||
    trimmed.startsWith("/*") ||
    trimmed.startsWith("*") ||
    trimmed.startsWith("import ") ||
    trimmed.startsWith("export ") ||
    trimmed.startsWith("case ") ||
    trimmed.startsWith("default") ||
    trimmed.startsWith("else") ||
    trimmed.startsWith("catch") ||
    trimmed.startsWith("finally") ||
    trimmed.startsWith("}") ||
    trimmed.startsWith("{") ||
    trimmed.startsWith("globalThis.__ariadnets_debug_") ||
    trimmed.includes("sourceMappingURL") ||
    trimmed.endsWith("{")
  ) {
    return false;
  }

  if (!line.startsWith("    ")) {
    return false;
  }

  return trimmed.endsWith(";");
}

function updateDebugState(state, line) {
  const code = stripLineStringsAndComments(line);
  for (let index = 0; index < code.length; ++index) {
    const character = code[index];
    if (character === "{") {
      const prefix = code.slice(0, index).trim();
      const parentVariables = currentDebugVariables(state);
      const blockInfo = createDebugBlockInfo(prefix, state.blockStack.some((block) => block.executable), parentVariables);
      state.blockStack.push({
        executable: blockInfo.executable,
        functionName: blockInfo.functionName,
        variables: blockInfo.variables,
      });
    } else if (character === "}") {
      state.blockStack.pop();
    } else if (character === "(" || character === "[") {
      ++state.expressionDepth;
    } else if (character === ")" || character === "]") {
      state.expressionDepth = Math.max(0, state.expressionDepth - 1);
    }
  }
  addDeclaredDebugVariables(state, code);
}

function createDebugBlockInfo(prefix, isInsideExecutableBlock, parentVariables) {
  const inheritedVariables = uniqueDebugVariables(parentVariables);
  if (isInsideExecutableBlock) {
    return {
      executable: true,
      functionName: currentFunctionNameFromPrefix(prefix, "block"),
      variables: inheritedVariables,
    };
  }

  if (/^(?:export\s+)?(?:default\s+)?class\b/.test(prefix)) {
    return {
      executable: false,
      functionName: "",
      variables: [],
    };
  }

  const executable = (
    /(?:^|\s)(?:async\s+)?function(?:\s+[A-Za-z_$][\w$]*)?\s*\([^)]*\)\s*$/.test(prefix) ||
    /^(?:async\s+)?constructor\s*\([^)]*\)\s*$/.test(prefix) ||
    /^(?:async\s+)?(?:get|set)\s+[A-Za-z_$][\w$]*\s*\([^)]*\)\s*$/.test(prefix) ||
    /^(?:async\s+)?[A-Za-z_$][\w$]*\s*\([^)]*\)\s*$/.test(prefix) ||
    /^(?:static\s+)?(?:async\s+)?[A-Za-z_$][\w$]*\s*\([^)]*\)\s*$/.test(prefix) ||
    /^(?:const|let|var)\s+[A-Za-z_$][\w$]*\s*=\s*(?:async\s*)?(?:\([^)]*\)|[A-Za-z_$][\w$]*)\s*=>\s*$/.test(prefix)
  );
  return {
    executable,
    functionName: executable ? currentFunctionNameFromPrefix(prefix, "anonymous") : "",
    variables: executable ? uniqueDebugVariables(["this", ...inheritedVariables, ...parseDebugParameters(prefix)]) : [],
  };
}

function currentFunctionNameFromPrefix(prefix, fallback) {
  const method = prefix.match(/^(?:static\s+)?(?:async\s+)?(?:get\s+|set\s+)?([A-Za-z_$][\w$]*)\s*\([^)]*\)\s*$/);
  if (method) {
    return method[1];
  }
  const fn = prefix.match(/function\s+([A-Za-z_$][\w$]*)\s*\([^)]*\)\s*$/);
  if (fn) {
    return fn[1];
  }
  const arrow = prefix.match(/^(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=/);
  if (arrow) {
    return arrow[1];
  }
  return fallback;
}

function parseDebugParameters(prefix) {
  const match = prefix.match(/\(([^)]*)\)\s*(?:=>)?\s*$/);
  if (!match) {
    return [];
  }
  return match[1]
    .split(",")
    .map((item) => item.trim())
    .filter((item) => /^[A-Za-z_$][\w$]*$/.test(item));
}

function currentDebugFunctionName(state) {
  for (let index = state.blockStack.length - 1; index >= 0; --index) {
    const block = state.blockStack[index];
    if (block.executable && block.functionName) {
      return block.functionName;
    }
  }
  return "AriadneTS";
}

function currentDebugVariables(state) {
  for (let index = state.blockStack.length - 1; index >= 0; --index) {
    const block = state.blockStack[index];
    if (block.executable) {
      return block.variables ?? [];
    }
  }
  return [];
}

function addDeclaredDebugVariables(state, code) {
  const variables = currentDebugVariables(state);
  if (variables.length === 0 && !state.blockStack.some((block) => block.executable)) {
    return;
  }
  for (const name of parseDeclaredDebugVariables(code)) {
    if (!variables.includes(name)) {
      variables.push(name);
    }
  }
}

function parseDeclaredDebugVariables(code) {
  const declaration = code.match(/\b(?:const|let|var)\s+(.+?)(?:;|$)/);
  if (!declaration) {
    return [];
  }
  const names = [];
  for (const part of splitTopLevelCommaList(declaration[1])) {
    const match = part.trim().match(/^([A-Za-z_$][\w$]*)\b/);
    if (match) {
      names.push(match[1]);
    }
  }
  return names;
}

function splitTopLevelCommaList(text) {
  const parts = [];
  let start = 0;
  let depth = 0;
  let quote = "";
  let escaped = false;
  for (let index = 0; index < text.length; ++index) {
    const character = text[index];
    if (quote) {
      if (escaped) {
        escaped = false;
      } else if (character === "\\") {
        escaped = true;
      } else if (character === quote) {
        quote = "";
      }
      continue;
    }
    if (character === "\"" || character === "'" || character === "`") {
      quote = character;
    } else if (character === "(" || character === "[" || character === "{") {
      ++depth;
    } else if (character === ")" || character === "]" || character === "}") {
      depth = Math.max(0, depth - 1);
    } else if (character === "," && depth === 0) {
      parts.push(text.slice(start, index));
      start = index + 1;
    }
  }
  parts.push(text.slice(start));
  return parts;
}

function uniqueDebugVariables(variables) {
  return [...new Set(variables.filter((name) => typeof name === "string" && name.length > 0))];
}

function createDebugVariablesCapture(state) {
  const variables = currentDebugVariables(state);
  if (variables.length === 0) {
    return "undefined";
  }
  const assignments = variables.map((name) =>
    `try{__s(${JSON.stringify(name)},${name});}catch{__v[${JSON.stringify(name)}]="<unavailable>";}`,
  ).join("");
  return `(()=>{const __v={};const __seen=[];const __p=(v)=>{const t=typeof v;if(t==="undefined")return "<undefined>";if(t==="bigint")return v.toString()+"n";if(t==="symbol")return String(v);if(t==="function")return "[Function"+(v.name?" "+v.name:"")+"]";return v;};const __c=(v,d)=>{try{v=__p(v);if(v===null||typeof v!=="object")return v;if(d<=0)return Array.isArray(v)?"Array":"Object";if(__seen.indexOf(v)>=0)return "<circular>";__seen.push(v);if(Array.isArray(v)){const a=[];for(let i=0;i<Math.min(v.length,32);++i)a.push(__c(v[i],d-1));if(v.length>32)a.push("...");return a;}const o={};let n=0;for(const k of Object.keys(v)){if(n++>=32){o["..."]="...";break;}o[k]=__c(v[k],d-1);}return o;}catch{return "<unavailable>";}};const __s=(n,v)=>{__v[n]=__c(v,2);};${assignments}return __v;})()`;
}

function stripLineStringsAndComments(line) {
  let result = "";
  let quote = "";
  let escaped = false;
  for (let index = 0; index < line.length; ++index) {
    const character = line[index];
    const next = line[index + 1];
    if (!quote && character === "/" && next === "/") {
      break;
    }
    if (!quote && (character === "\"" || character === "'" || character === "`")) {
      quote = character;
      result += " ";
      continue;
    }
    if (quote) {
      if (escaped) {
        escaped = false;
      } else if (character === "\\") {
        escaped = true;
      } else if (character === quote) {
        quote = "";
      }
      result += " ";
      continue;
    }
    result += character;
  }
  return result;
}

async function validateRuntimeJavaScript(distDirectory) {
  for (const relativePath of await listRuntimeFiles(distDirectory)) {
    if (!relativePath.endsWith(".js")) {
      continue;
    }

    const fullPath = path.join(distDirectory, relativePath);
    const lines = (await readFile(fullPath, "utf8")).split(/(?<=\n)/);
    const debugState = {
      blockStack: [],
      expressionDepth: 0,
    };
    for (let index = 0; index < lines.length; ++index) {
      const line = lines[index];
      const stripped = stripLineStringsAndComments(line);
      if (stripped.includes("?.") || stripped.includes("??")) {
        throw new Error(
          `Generated JavaScript contains syntax unsupported by the embedded runtime: ${relativePath}:${index + 1}`,
        );
      }
      if (
        debugState.expressionDepth > 0 &&
        line.trim().startsWith("globalThis.__ariadnets_debug_")
      ) {
        throw new Error(
          `Debug probe was inserted inside a multi-line expression: ${relativePath}:${index + 1}`,
        );
      }
      updateDebugState(debugState, line);
    }
  }
}

async function readSourceMap(distDirectory, relativePath) {
  const mapPath = path.join(distDirectory, `${relativePath}.map`);
  if (!existsSync(mapPath)) {
    return null;
  }

  try {
    return JSON.parse(await readFile(mapPath, "utf8"));
  } catch (error) {
    throw new Error(`Could not read source map for ${relativePath}: ${error.message}`);
  }
}

function resolveOriginalPosition(distDirectory, relativePath, sourceMap, generatedLine, generatedColumn) {
  const fallback = {
    source: relativePath.replaceAll(path.sep, "/"),
    line: generatedLine,
    column: generatedColumn,
  };
  if (!sourceMap || typeof sourceMap.mappings !== "string" || !Array.isArray(sourceMap.sources)) {
    return fallback;
  }

  const mapping = findBestSourceMapSegment(sourceMap.mappings, generatedLine, generatedColumn);
  if (!mapping || mapping.sourceIndex < 0 || mapping.sourceIndex >= sourceMap.sources.length) {
    return fallback;
  }

  const sourceRoot = typeof sourceMap.sourceRoot === "string" ? sourceMap.sourceRoot : "";
  const sourcePath = sourceMap.sources[mapping.sourceIndex];
  const absoluteSourcePath = path.resolve(
    path.dirname(path.join(distDirectory, relativePath)),
    sourceRoot,
    sourcePath,
  );
  const sourceRelativePath = path.relative(path.dirname(distDirectory), absoluteSourcePath);
  return {
    source: sourceRelativePath.replaceAll(path.sep, "/"),
    line: mapping.sourceLine + 1,
    column: mapping.sourceColumn,
  };
}

function findBestSourceMapSegment(mappings, generatedLine, generatedColumn) {
  const lines = mappings.split(";");
  if (generatedLine < 1 || generatedLine > lines.length) {
    return null;
  }

  let generatedColumnState = 0;
  let sourceIndexState = 0;
  let sourceLineState = 0;
  let sourceColumnState = 0;
  for (let lineIndex = 0; lineIndex < generatedLine - 1; ++lineIndex) {
    generatedColumnState = 0;
    for (const segment of lines[lineIndex].split(",")) {
      if (!segment) {
        continue;
      }
      const values = decodeVlqSegment(segment);
      generatedColumnState += values[0] ?? 0;
      if (values.length >= 4) {
        sourceIndexState += values[1];
        sourceLineState += values[2];
        sourceColumnState += values[3];
      }
    }
  }

  generatedColumnState = 0;
  let best = null;
  for (const segment of lines[generatedLine - 1].split(",")) {
    if (!segment) {
      continue;
    }

    const values = decodeVlqSegment(segment);
    generatedColumnState += values[0] ?? 0;
    if (values.length >= 4) {
      sourceIndexState += values[1];
      sourceLineState += values[2];
      sourceColumnState += values[3];
      if (generatedColumnState <= generatedColumn) {
        best = {
          sourceIndex: sourceIndexState,
          sourceLine: sourceLineState,
          sourceColumn: sourceColumnState,
        };
      }
    }
    if (generatedColumnState > generatedColumn) {
      break;
    }
  }
  return best;
}

function decodeVlqSegment(segment) {
  const values = [];
  let value = 0;
  let shift = 0;
  for (const character of segment) {
    let digit = base64VlqValue(character);
    const continuation = (digit & 32) !== 0;
    digit &= 31;
    value += digit << shift;
    if (continuation) {
      shift += 5;
      continue;
    }

    const negative = (value & 1) === 1;
    values.push((negative ? -1 : 1) * (value >> 1));
    value = 0;
    shift = 0;
  }
  return values;
}

function base64VlqValue(character) {
  const code = character.charCodeAt(0);
  if (code >= 65 && code <= 90) {
    return code - 65;
  }
  if (code >= 97 && code <= 122) {
    return code - 97 + 26;
  }
  if (code >= 48 && code <= 57) {
    return code - 48 + 52;
  }
  if (character === "+") {
    return 62;
  }
  if (character === "/") {
    return 63;
  }
  throw new Error(`Invalid source map VLQ character: ${character}`);
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
