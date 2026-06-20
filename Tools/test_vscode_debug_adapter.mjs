import { spawn } from "node:child_process";
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

const server = net.createServer((socket) => {
  socket.setEncoding("utf8");
  socket.write(
    "AriadneTS debug endpoint connected.\n" +
    "Commands: status, continue, break <file>:<line>, clear <file>:<line>, breakpoints\n" +
    "Native breakpoint protocol is not implemented yet.\n",
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
    pollIntervalMs: 1000,
  });
  const attach = await waitForResponse("attach");
  assert(attach.success, "attach failed");

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

  sendRequest("disconnect", {});
  const disconnect = await waitForResponse("disconnect");
  assert(disconnect.success, "disconnect failed");
} finally {
  adapter.kill();
  server.close();
}

await testRealRuntimeBreakpoint();
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
      const localsReference = scopes.body?.scopes?.[0]?.variablesReference;
      assert(localsReference > 0, "locals scope reference missing");
      dap.sendRequest("variables", { variablesReference: localsReference });
      const variables = await dap.waitForResponse("variables");
      assert(
        variables.body?.variables?.some((item) => item.name === "payload" && item.value === "Object"),
        "payload local variable missing",
      );

      dap.sendRequest("next", { threadId: 1 });
      assert((await dap.waitForResponse("next")).success, "integration next failed");
      await dap.waitForEvent("stopped", 10000);
      dap.sendRequest("stackTrace", { threadId: 1 });
      const nextStackTrace = await dap.waitForResponse("stackTrace");
      const nextFrame = nextStackTrace.body?.stackFrames?.[0];
      assert(nextFrame?.line === 88, `next stack line did not match: ${nextFrame?.line}`);

      dap.sendRequest("continue", { threadId: 1 });
      assert((await dap.waitForResponse("continue")).success, "integration continue failed");
      await waitForBufferedOutput(() => runtimeOutput, "DONE", 10000);
    } finally {
      integrationAdapter.kill();
    }
  } finally {
    runtime.kill();
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
