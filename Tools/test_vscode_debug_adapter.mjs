import { spawn } from "node:child_process";
import fs from "node:fs";
import net from "node:net";
import path from "node:path";

const root = path.resolve(new URL("..", import.meta.url).pathname);
const adapterPath = path.join(
  root,
  "UnityPackages",
  "com.ariadnets.runtime",
  "Editor",
  "Tools",
  "vscode-ariadnets-debugger",
  "adapter",
  "ariadnets-debug-adapter.mjs",
);
const port = 49390;
const receivedRuntimeCommands = [];
const tracePath = path.join(root, "UnityProject", "TypeScript", ".ariadnets", "debug-adapter-test.log");
fs.rmSync(tracePath, { force: true });

const server = net.createServer((socket) => {
  socket.setEncoding("utf8");
  socket.write(
    "AriadneTS debug endpoint connected.\n" +
    "Commands: status, continue, step, next, stepIn, stepOut, variables, stack, break <file>:<line>, clear <file>:<line>, breakpoints\n",
  );
  socket.on("data", (chunk) => {
    const command = JSON.parse(chunk.trim());
    receivedRuntimeCommands.push(command);
    if (command.command === "listBreakpoints") {
      socket.end("{\"breakpoints\":[]}\n");
    } else if (command.command === "setBreakpoint") {
      socket.end("{\"ok\":true}\n");
    } else if (command.command === "status") {
      socket.end("{\"state\":\"running\",\"module\":\"\",\"line\":0,\"column\":0}\n");
    } else if (command.command === "continue") {
      socket.end("{\"ok\":true,\"continued\":true}\n");
    } else {
      socket.end("{\"ok\":true}\n");
    }
  });
});

await new Promise((resolve) => server.listen(port, "127.0.0.1", resolve));

const adapter = spawn(process.execPath, [adapterPath], {
  cwd: root,
  stdio: ["pipe", "pipe", "inherit"],
});

let adapterBuffer = Buffer.alloc(0);
const responses = [];
const events = [];
adapter.stdout.on("data", (chunk) => {
  adapterBuffer = Buffer.concat([adapterBuffer, chunk]);
  while (true) {
    const headerEnd = adapterBuffer.indexOf("\r\n\r\n");
    if (headerEnd < 0) {
      return;
    }
    const header = adapterBuffer.subarray(0, headerEnd).toString("utf8");
    const match = header.match(/Content-Length:\s*(\d+)/i);
    if (!match) {
      throw new Error(`Invalid DAP header: ${header}`);
    }
    const length = Number.parseInt(match[1], 10);
    const start = headerEnd + 4;
    const end = start + length;
    if (adapterBuffer.length < end) {
      return;
    }
    const message = JSON.parse(adapterBuffer.subarray(start, end).toString("utf8"));
    adapterBuffer = adapterBuffer.subarray(end);
    if (message.type === "response") {
      responses.push(message);
    } else if (message.type === "event") {
      events.push(message);
    }
  }
});

let nextSeq = 1;
function sendRequest(command, args = {}) {
  const message = {
    seq: nextSeq++,
    type: "request",
    command,
    arguments: args,
  };
  const payload = Buffer.from(JSON.stringify(message), "utf8");
  adapter.stdin.write(`Content-Length: ${payload.length}\r\n\r\n`);
  adapter.stdin.write(payload);
  return message.seq;
}

async function waitForResponse(command, timeoutMs = 3000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const response = responses.find((item) => item.command === command);
    if (response) {
      return response;
    }
    await sleep(25);
  }
  throw new Error(`Timed out waiting for ${command} response`);
}

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

try {
  sendRequest("initialize", {});
  const initialize = await waitForResponse("initialize");
  assert(initialize.success, "initialize failed");
  assert(events.some((event) => event.event === "initialized"), "initialized event missing");

  sendRequest("attach", {
    host: "127.0.0.1",
    port,
    tsRoot: path.join(root, "UnityProject", "TypeScript"),
    pollIntervalMs: 50,
    tracePath,
  });
  const attach = await waitForResponse("attach");
  assert(attach.success, "attach failed");

  sendRequest("setExceptionBreakpoints", { filters: [] });
  const setExceptionBreakpoints = await waitForResponse("setExceptionBreakpoints");
  assert(setExceptionBreakpoints.success, "setExceptionBreakpoints failed");

  sendRequest("setBreakpoints", {
    source: {
      path: path.join(root, "UnityProject", "TypeScript", "src", "game-application.ts"),
    },
    breakpoints: [{ line: 77 }],
  });
  const setBreakpoints = await waitForResponse("setBreakpoints");
  assert(setBreakpoints.success, "setBreakpoints failed");
  assert(setBreakpoints.body?.breakpoints?.[0]?.line === 80, "breakpoint should bind line 77 to executable line 80");
  assert(
    receivedRuntimeCommands.some(
      (command) =>
        command.command === "setBreakpoint" &&
        command.module === "src/game-application.ts" &&
        command.line === 80,
    ),
    "adapter did not forward setBreakpoint",
  );

  sendRequest("configurationDone", {});
  const configurationDone = await waitForResponse("configurationDone");
  assert(configurationDone.success, "configurationDone failed");
  const appliedBreakpointCount = receivedRuntimeCommands.filter(
    (command) => command.command === "setBreakpoint",
  ).length;
  await sleep(250);
  assert(
    receivedRuntimeCommands.filter((command) => command.command === "setBreakpoint").length ===
      appliedBreakpointCount,
    "adapter reapplied unchanged breakpoints during status polling",
  );

  sendRequest("stepIn", { threadId: 1 });
  const stepIn = await waitForResponse("stepIn");
  assert(stepIn.success, "stepIn failed");
  sendRequest("stepOut", { threadId: 1 });
  const stepOut = await waitForResponse("stepOut");
  assert(stepOut.success, "stepOut failed");
  assert(
    receivedRuntimeCommands.some((command) => command.command === "stepIn"),
    "adapter did not forward stepIn",
  );
  assert(
    receivedRuntimeCommands.some((command) => command.command === "stepOut"),
    "adapter did not forward stepOut",
  );
  const trace = fs.readFileSync(tracePath, "utf8");
  assert(trace.includes("\"command\":\"stepIn\""), "trace did not record stepIn");
  assert(trace.includes("runtime.send"), "trace did not record runtime commands");

  sendRequest("disconnect", {});
  const disconnect = await waitForResponse("disconnect");
  assert(disconnect.success, "disconnect failed");
  assert(
    receivedRuntimeCommands.some((command) => command.command === "continue"),
    "adapter did not resume runtime on disconnect",
  );
} finally {
  adapter.kill();
  server.close();
}

await testRealRuntimeBreakpoint();
await testSourceMappedStackFrames();
console.log("vscode debug adapter smoke test passed");

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

async function testRealRuntimeBreakpoint() {
  const runtimePort = 49391;
  const runtime = spawn(
    "dotnet",
    [
      "run",
      "--project",
      "ManagedTests",
      "--no-restore",
      "--",
      "--debug-adapter-runtime-fixture",
      String(runtimePort),
    ],
    {
      cwd: root,
      env: {
        ...process.env,
        DYLD_LIBRARY_PATH: path.join(root, "Build", "native"),
      },
      stdio: ["pipe", "pipe", "inherit"],
    },
  );
  let runtimeOutput = "";
  runtime.stdout.on("data", (chunk) => {
    runtimeOutput += chunk.toString("utf8");
  });

  try {
    await waitForBufferedOutput(() => runtimeOutput, "READY", 10000);

    const integrationAdapter = spawn(process.execPath, [adapterPath], {
      cwd: root,
      stdio: ["pipe", "pipe", "inherit"],
    });
    const dap = createDapHarness(integrationAdapter);
    try {
      dap.sendRequest("initialize", {});
      assert((await dap.waitForResponse("initialize")).success, "integration initialize failed");
      await dap.waitForEvent("initialized");

      dap.sendRequest("attach", {
        host: "127.0.0.1",
        port: runtimePort,
        tsRoot: path.join(root, "UnityProject", "TypeScript"),
        pollIntervalMs: 50,
      });
      assert((await dap.waitForResponse("attach")).success, "integration attach failed");

      dap.sendRequest("setBreakpoints", {
        source: {
          path: path.join(root, "UnityProject", "TypeScript", "src", "game-application.ts"),
        },
        breakpoints: [{ line: 87 }],
      });
      assert((await dap.waitForResponse("setBreakpoints")).success, "integration setBreakpoints failed");

      runtime.stdin.write("RUN\n");
      const stopped = await dap.waitForEvent("stopped", 10000);
      assert(stopped.body?.reason === "breakpoint", "integration stopped reason did not match");

      dap.sendRequest("stackTrace", { threadId: 1 });
      const stackTrace = await dap.waitForResponse("stackTrace");
      const frame = stackTrace.body?.stackFrames?.[0];
      assert(frame?.source?.path?.endsWith("UnityProject/TypeScript/src/game-application.ts"), "stack source did not map to TypeScript file");
      assert(frame?.line === 87, `stack line did not match: ${frame?.line}`);
      assert(frame?.name === "onTick", `stack frame name did not match: ${frame?.name}`);

      dap.sendRequest("scopes", { frameId: 1 });
      const scopes = await dap.waitForResponse("scopes");
      const scopeItems = scopes.body?.scopes ?? [];
      const localsScope = scopeItems.find((item) => item.name === "Locals");
      const runtimeScope = scopeItems.find((item) => item.name === "AriadneTS Runtime");
      const localsReference = localsScope?.variablesReference;
      const runtimeReference = runtimeScope?.variablesReference;
      assert(localsReference > 0, "locals scope reference missing");
      assert(runtimeReference > 0, "runtime scope reference missing");
      dap.sendRequest("variables", { variablesReference: localsReference });
      const variables = await dap.waitForResponse("variables");
      const payloadVariable = variables.body?.variables?.find((item) => item.name === "payload");
      const variableItems = variables.body?.variables ?? [];
      assert(
        payloadVariable?.value === "Object" && payloadVariable.variablesReference > 0,
        "payload local variable missing",
      );
      assert(variableItems.some((item) => item.name === "missing" && item.value === "<undefined>"), "undefined snapshot missing");
      assert(variableItems.some((item) => item.name === "callback" && item.value === "[Function tick]"), "function snapshot missing");
      assert(variableItems.some((item) => item.name === "big" && item.value === "9007199254740993n"), "bigint snapshot missing");
      assert(variableItems.some((item) => item.name === "token" && item.value === "Symbol(token)"), "symbol snapshot missing");
      assert(
        variableItems.some((item) => item.name === "circular" && item.value === "Object" && item.variablesReference > 0),
        "circular object snapshot missing",
      );
      dap.sendRequest("variables", { variablesReference: payloadVariable.variablesReference });
      const payloadFields = await dap.waitForResponse("variables");
      assert(
        payloadFields.body?.variables?.some((item) => item.name === "deltaTime" && item.value === "1.25"),
        "payload.deltaTime field missing",
      );

      dap.sendRequest("variables", { variablesReference: runtimeReference });
      const runtimeVariables = await dap.waitForResponse("variables");
      const runtimeItems = runtimeVariables.body?.variables ?? [];
      assert(runtimeItems.some((item) => item.name === "state" && item.value === "paused"), "runtime state missing");
      assert(runtimeItems.some((item) => item.name === "function" && item.value === "onTick"), "runtime function missing");
      assert(runtimeItems.some((item) => item.name === "line" && item.value === "87"), "runtime line missing");
      assert(runtimeItems.some((item) => item.name === "endpoint" && item.value === `127.0.0.1:${runtimePort}`), "runtime endpoint missing");

      dap.sendRequest("evaluate", { expression: "payload.deltaTime", frameId: 1, context: "watch" });
      const evaluatedPayload = await dap.waitForResponse("evaluate");
      assert(
        evaluatedPayload.body?.result === "1.25",
        `payload.deltaTime evaluation did not match: ${evaluatedPayload.body?.result}`,
      );

      dap.sendRequest("evaluate", { expression: "$runtime.function", frameId: 1, context: "watch" });
      const evaluatedRuntime = await dap.waitForResponse("evaluate");
      assert(
        evaluatedRuntime.body?.result === "onTick",
        `runtime function evaluation did not match: ${evaluatedRuntime.body?.result}`,
      );

      dap.sendRequest("next", { threadId: 1 });
      assert((await dap.waitForResponse("next")).success, "integration next failed");
      const nextContinued = await dap.waitForEvent("continued", 10000);
      assert(nextContinued.body?.threadId === 1, "next continued thread did not match");
      const stepStopped = await dap.waitForEvent("stopped", 10000);
      assert(stepStopped.body?.reason === "step", "next stopped reason should be step");
      dap.sendRequest("stackTrace", { threadId: 1 });
      const nextStackTrace = await dap.waitForResponse("stackTrace");
      const nextFrame = nextStackTrace.body?.stackFrames?.[0];
      assert(nextFrame?.line === 88, `next stack line did not match: ${nextFrame?.line}`);

      dap.sendRequest("continue", { threadId: 1 });
      assert((await dap.waitForResponse("continue")).success, "integration continue failed");
      const continueEvent = await dap.waitForEvent("continued", 10000);
      assert(continueEvent.body?.allThreadsContinued === true, "continue event missing allThreadsContinued");
      await waitForBufferedOutput(() => runtimeOutput, "DONE", 10000);
    } finally {
      integrationAdapter.kill();
    }
  } finally {
    runtime.kill();
  }
}

async function testSourceMappedStackFrames() {
  const sourceMapPort = 49392;
  const server = net.createServer((socket) => {
    socket.setEncoding("utf8");
    socket.write(
      "AriadneTS debug endpoint connected.\n" +
      "Commands: status, continue, step, next, stepIn, stepOut, variables, stack, break <file>:<line>, clear <file>:<line>, breakpoints\n",
    );
    socket.on("data", (chunk) => {
      const command = JSON.parse(chunk.trim());
      if (command.command === "listBreakpoints") {
        socket.end("{\"breakpoints\":[]}\n");
      } else if (command.command === "status") {
        socket.end("{\"state\":\"paused\",\"pauseId\":1,\"module\":\"src/game-application.ts\",\"function\":\"onTick\",\"line\":87,\"column\":4}\n");
      } else if (command.command === "stack") {
        socket.end("{\"stack\":\"at debugProbe (src/bootstrap.js:20:7)\\nat GameApplication.onTick (src/game-application.js:70:9)\"}\n");
      } else if (command.command === "variables") {
        socket.end("{\"variables\":{\"payload\":{\"deltaTime\":1.25}}}\n");
      } else {
        socket.end("{\"ok\":true}\n");
      }
    });
  });
  await new Promise((resolve) => server.listen(sourceMapPort, "127.0.0.1", resolve));

  const sourceMapAdapter = spawn(process.execPath, [adapterPath], {
    cwd: root,
    stdio: ["pipe", "pipe", "inherit"],
  });
  const dap = createDapHarness(sourceMapAdapter);
  try {
    dap.sendRequest("initialize", {});
    assert((await dap.waitForResponse("initialize")).success, "source-map initialize failed");
    await dap.waitForEvent("initialized");

    dap.sendRequest("attach", {
      host: "127.0.0.1",
      port: sourceMapPort,
      tsRoot: path.join(root, "UnityProject", "TypeScript"),
      pollIntervalMs: 50,
    });
    assert((await dap.waitForResponse("attach")).success, "source-map attach failed");
    await dap.waitForEvent("stopped", 5000);

    dap.sendRequest("stackTrace", { threadId: 1 });
    const stackTrace = await dap.waitForResponse("stackTrace");
    const frames = stackTrace.body?.stackFrames ?? [];
    assert(frames.length >= 2, "source-mapped stack should include multiple frames");
    assert(
      frames[1]?.source?.path?.endsWith("UnityProject/TypeScript/src/game-application.ts"),
      `generated JS frame did not map to TypeScript: ${frames[1]?.source?.path}`,
    );
  } finally {
    sourceMapAdapter.kill();
    server.close();
  }
}

function createDapHarness(processHandle) {
  let buffer = Buffer.alloc(0);
  const harnessResponses = [];
  const harnessEvents = [];
  let harnessSeq = 1;
  processHandle.stdout.on("data", (chunk) => {
    buffer = Buffer.concat([buffer, chunk]);
    while (true) {
      const headerEnd = buffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) {
        return;
      }
      const header = buffer.subarray(0, headerEnd).toString("utf8");
      const match = header.match(/Content-Length:\s*(\d+)/i);
      if (!match) {
        throw new Error(`Invalid DAP header: ${header}`);
      }
      const length = Number.parseInt(match[1], 10);
      const start = headerEnd + 4;
      const end = start + length;
      if (buffer.length < end) {
        return;
      }
      const message = JSON.parse(buffer.subarray(start, end).toString("utf8"));
      buffer = buffer.subarray(end);
      if (message.type === "response") {
        harnessResponses.push(message);
      } else if (message.type === "event") {
        harnessEvents.push(message);
      }
    }
  });

  return {
    sendRequest(command, args = {}) {
      const message = {
        seq: harnessSeq++,
        type: "request",
        command,
        arguments: args,
      };
      const payload = Buffer.from(JSON.stringify(message), "utf8");
      processHandle.stdin.write(`Content-Length: ${payload.length}\r\n\r\n`);
      processHandle.stdin.write(payload);
      return message.seq;
    },
    async waitForResponse(command, timeoutMs = 3000) {
      const deadline = Date.now() + timeoutMs;
      while (Date.now() < deadline) {
        const index = harnessResponses.findIndex((item) => item.command === command);
        if (index >= 0) {
          return harnessResponses.splice(index, 1)[0];
        }
        await sleep(25);
      }
      throw new Error(`Timed out waiting for ${command} response`);
    },
    async waitForEvent(event, timeoutMs = 3000) {
      const deadline = Date.now() + timeoutMs;
      while (Date.now() < deadline) {
        const index = harnessEvents.findIndex((item) => item.event === event);
        if (index >= 0) {
          return harnessEvents.splice(index, 1)[0];
        }
        await sleep(25);
      }
      throw new Error(`Timed out waiting for ${event} event`);
    },
  };
}

async function waitForBufferedOutput(readOutput, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const output = readOutput();
    if (output.includes(expected)) {
      return output;
    }
    await sleep(25);
  }
  throw new Error(`Timed out waiting for process output: ${expected}. Output: ${readOutput()}`);
}
