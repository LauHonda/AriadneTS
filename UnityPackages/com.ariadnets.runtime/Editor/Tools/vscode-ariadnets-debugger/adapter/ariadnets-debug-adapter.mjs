#!/usr/bin/env node
import fs from "node:fs";
import net from "node:net";
import path from "node:path";

class DapConnection {
  constructor(input, output) {
    this.input = input;
    this.output = output;
    this.buffer = Buffer.alloc(0);
    this.nextSeq = 1;
    this.handlers = new Map();

    input.on("data", (chunk) => this.onData(chunk));
  }

  on(command, handler) {
    this.handlers.set(command, handler);
  }

  onData(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    while (true) {
      const headerEnd = this.buffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) {
        return;
      }

      const header = this.buffer.subarray(0, headerEnd).toString("utf8");
      const match = header.match(/Content-Length:\s*(\d+)/i);
      if (!match) {
        this.buffer = this.buffer.subarray(headerEnd + 4);
        continue;
      }

      const length = Number.parseInt(match[1], 10);
      const messageStart = headerEnd + 4;
      const messageEnd = messageStart + length;
      if (this.buffer.length < messageEnd) {
        return;
      }

      const payload = this.buffer.subarray(messageStart, messageEnd).toString("utf8");
      this.buffer = this.buffer.subarray(messageEnd);
      let message;
      try {
        message = JSON.parse(payload);
      } catch {
        continue;
      }
      void this.dispatch(message);
    }
  }

  async dispatch(message) {
    if (message.type !== "request") {
      return;
    }

    const handler = this.handlers.get(message.command);
    if (!handler) {
      this.sendResponse(message, false, undefined, `Unsupported request: ${message.command}`);
      return;
    }

    try {
      const body = await handler(message.arguments ?? {}, message);
      this.sendResponse(message, true, body);
    } catch (error) {
      this.sendResponse(message, false, undefined, error instanceof Error ? error.message : String(error));
    }
  }

  send(message) {
    const payload = Buffer.from(JSON.stringify(message), "utf8");
    this.output.write(`Content-Length: ${payload.length}\r\n\r\n`);
    this.output.write(payload);
  }

  sendResponse(request, success, body, message) {
    const response = {
      seq: this.nextSeq++,
      type: "response",
      request_seq: request.seq,
      command: request.command,
      success,
    };
    if (body !== undefined) {
      response.body = body;
    }
    if (!success) {
      response.message = message ?? "Request failed";
    }
    this.send(response);
  }

  sendEvent(event, body) {
    const message = {
      seq: this.nextSeq++,
      type: "event",
      event,
    };
    if (body !== undefined) {
      message.body = body;
    }
    this.send(message);
  }
}

class AriadneTsClient {
  constructor() {
    this.host = "127.0.0.1";
    this.port = 9229;
    this.timeoutMs = 750;
  }

  configure(options) {
    this.host = options.host ?? this.host;
    this.port = Number(options.port ?? this.port);
  }

  async command(payload) {
    return await new Promise((resolve, reject) => {
      const socket = net.createConnection({ host: this.host, port: this.port });
      let response = "";
      let sawGreeting = false;
      let settled = false;
      const finish = (error) => {
        if (settled) {
          return;
        }
        if (error && !(sawGreeting && response.trim())) {
          settled = true;
          clearTimeout(timeout);
          reject(error);
          return;
        }
        if (!sawGreeting) {
          settled = true;
          clearTimeout(timeout);
          reject(new Error(`AriadneTS did not send a debugger greeting at ${this.host}:${this.port}`));
          return;
        }
        const text = response.trim();
        settled = true;
        clearTimeout(timeout);
        if (!text) {
          resolve({});
          return;
        }
        try {
          resolve(JSON.parse(text));
        } catch {
          resolve({ text });
        }
      };
      const timeout = setTimeout(() => {
        socket.destroy();
        finish(new Error(`Timed out connecting to AriadneTS at ${this.host}:${this.port}`));
      }, this.timeoutMs);

      socket.setEncoding("utf8");
      socket.on("connect", () => {});
      socket.on("data", (chunk) => {
        response += chunk;
        if (!sawGreeting && response.includes("Native breakpoint protocol")) {
          sawGreeting = true;
          response = "";
          socket.write(`${JSON.stringify(payload)}\n`);
        }
      });
      socket.on("error", (error) => {
        finish(error);
      });
      socket.on("close", () => {
        finish();
      });
    });
  }
}

class AriadneDebugSession {
  constructor() {
    this.dap = new DapConnection(process.stdin, process.stdout);
    this.client = new AriadneTsClient();
    this.tsRoot = path.resolve("TypeScript");
    this.pollIntervalMs = 250;
    this.pollTimer = undefined;
    this.pendingBreakpoints = new Map();
    this.probeLinesByModule = new Map();
    this.appliedBreakpointKey = "";
    this.variableHandles = new Map();
    this.nextVariableHandle = 1;
    this.lastStatus = { state: "running", module: "", line: 0, column: 0 };
    this.wasPaused = false;
    this.wasRuntimeConnected = false;
    this.threadId = 1;

    this.registerHandlers();
  }

  registerHandlers() {
    this.dap.on("initialize", async () => {
      queueMicrotask(() => this.dap.sendEvent("initialized"));
      return {
        supportsConfigurationDoneRequest: true,
        supportsTerminateRequest: true,
        supportsStepInRequest: true,
        supportsStepOutRequest: true,
        supportsNextRequest: true,
        supportsSetVariable: false,
        supportsEvaluateForHovers: false,
        supportsStepBack: false,
      };
    });

    this.dap.on("launch", async (args) => {
      this.configure(args);
      this.startPolling();
      return {};
    });

    this.dap.on("attach", async (args) => {
      this.configure(args);
      this.startPolling();
      return {};
    });

    this.dap.on("configurationDone", async () => {
      await this.applyAllBreakpointsBestEffort();
      return {};
    });

    this.dap.on("setBreakpoints", async (args) => this.setBreakpoints(args));
    this.dap.on("threads", async () => ({
      threads: [{ id: this.threadId, name: "AriadneTS Runtime" }],
    }));
    this.dap.on("stackTrace", async () => this.stackTrace());
    this.dap.on("scopes", async () => this.scopes());
    this.dap.on("variables", async (args) => this.variables(args));
    this.dap.on("continue", async () => {
      await this.client.command({ command: "continue" });
      this.wasPaused = false;
      return { allThreadsContinued: true };
    });
    this.dap.on("next", async () => this.step("next"));
    this.dap.on("stepIn", async () => this.step("stepIn"));
    this.dap.on("stepOut", async () => this.step("stepOut"));
    this.dap.on("disconnect", async () => {
      this.stopPolling();
      return {};
    });
    this.dap.on("terminate", async () => {
      this.stopPolling();
      this.dap.sendEvent("terminated");
      return {};
    });
  }

  configure(args) {
    this.client.configure(args);
    if (args.tsRoot) {
      this.tsRoot = path.resolve(args.tsRoot);
    }
    this.refreshProbeIndex();
    if (args.pollIntervalMs) {
      this.pollIntervalMs = Math.max(100, Number(args.pollIntervalMs));
    }
  }

  async setBreakpoints(args) {
    const sourcePath = args.source?.path ?? args.source?.name;
    const modulePath = this.toModulePath(sourcePath);
    const requestedLines = (args.breakpoints ?? []).map((breakpoint) => Number(breakpoint.line));
    const resolvedLines = requestedLines.map((line) => this.resolveBreakpointLine(modulePath, line));
    this.pendingBreakpoints.set(modulePath, resolvedLines);

    await this.applyBreakpointsBestEffort(modulePath, resolvedLines);
    return {
      breakpoints: requestedLines.map((line, index) => ({
        verified: Number.isFinite(resolvedLines[index]),
        line: resolvedLines[index] ?? line,
        source: { path: sourcePath },
        message: resolvedLines[index] === line
          ? undefined
          : `Bound to nearest AriadneTS executable line ${resolvedLines[index]}.`,
      })),
    };
  }

  async applyAllBreakpointsBestEffort() {
    for (const [modulePath, lines] of this.pendingBreakpoints) {
      await this.applyBreakpointsBestEffort(modulePath, lines);
    }
  }

  async applyBreakpointsBestEffort(modulePath, lines) {
    const breakpointKey = JSON.stringify([...this.pendingBreakpoints].sort());
    const shouldLogApply = breakpointKey !== this.appliedBreakpointKey;
    try {
      const current = await this.client.command({ command: "listBreakpoints" });
      for (const breakpoint of current.breakpoints ?? []) {
        if (breakpoint.module === modulePath) {
          await this.client.command({
            command: "clearBreakpoint",
            module: modulePath,
            line: breakpoint.line,
          });
        }
      }
      for (const line of lines) {
        await this.client.command({
          command: "setBreakpoint",
          module: modulePath,
          line,
        });
      }
      this.appliedBreakpointKey = breakpointKey;
      if (shouldLogApply && lines.length > 0) {
        this.output(`Applied ${lines.length} breakpoint(s) to ${modulePath} on ${this.client.host}:${this.client.port}.`);
      }
    } catch (error) {
      this.output(`Waiting for AriadneTS runtime at ${this.client.host}:${this.client.port}: ${error instanceof Error ? error.message : String(error)}`);
      // The Unity/Unreal runtime may not be in Play Mode yet. Polling will retry.
    }
  }

  startPolling() {
    if (this.pollTimer) {
      return;
    }
    this.pollTimer = setInterval(() => {
      void this.pollStatus();
    }, this.pollIntervalMs);
    void this.pollStatus();
  }

  stopPolling() {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = undefined;
    }
  }

  async pollStatus() {
    try {
      await this.applyAllBreakpointsBestEffort();
      const status = await this.client.command({ command: "status" });
      if (!this.wasRuntimeConnected) {
        this.output(`Connected to AriadneTS runtime at ${this.client.host}:${this.client.port}.`);
      }
      this.wasRuntimeConnected = true;
      this.lastStatus = status;
      const isPaused = status.state === "paused";
      if (isPaused && !this.wasPaused) {
        this.wasPaused = true;
        this.variableHandles.clear();
        this.nextVariableHandle = 1;
        this.dap.sendEvent("stopped", {
          reason: "breakpoint",
          threadId: this.threadId,
          allThreadsStopped: true,
        });
      } else if (!isPaused) {
        this.wasPaused = false;
      }
    } catch (error) {
      if (this.wasRuntimeConnected) {
        this.output(`Lost AriadneTS runtime connection: ${error instanceof Error ? error.message : String(error)}`);
      }
      this.wasRuntimeConnected = false;
      // Runtime not available yet. Keep polling quietly.
    }
  }

  async stackTrace() {
    const status = this.lastStatus?.state === "paused"
      ? this.lastStatus
      : await this.safeStatus();
    const runtimeStack = await this.safeStack();
    const frames = this.parseRuntimeStack(runtimeStack, status);
    if (frames.length > 0) {
      return {
        stackFrames: frames,
        totalFrames: frames.length,
      };
    }

    const fallback = this.createStackFrame(status, 1);
    return {
      stackFrames: [fallback],
      totalFrames: 1,
    };
  }

  async safeStatus() {
    try {
      return await this.client.command({ command: "status" });
    } catch {
      return this.lastStatus;
    }
  }

  async safeStack() {
    if (this.lastStatus?.state !== "paused") {
      return "";
    }
    try {
      const response = await this.client.command({ command: "stack" });
      return typeof response.stack === "string" ? response.stack : "";
    } catch {
      return "";
    }
  }

  parseRuntimeStack(stack, status) {
    const frames = [];
    const statusFrame = this.createStackFrame(status, 1);
    if (!stack) {
      return [statusFrame];
    }

    for (const line of stack.split(/\r?\n/)) {
      const frame = this.parseStackLine(line, frames.length + 1);
      if (frame) {
        frames.push(frame);
      }
    }

    if (frames.length === 0) {
      return [statusFrame];
    }

    // The native pause location is source-map aware. QuickJS stack lines are often generated JS,
    // so the top frame is normalized to the exact TS line where the probe paused.
    frames[0] = {
      ...frames[0],
      id: 1,
      name: status.function || frames[0].name || "AriadneTS",
      source: statusFrame.source,
      line: statusFrame.line,
      column: statusFrame.column,
    };
    return frames;
  }

  parseStackLine(line, id) {
    const trimmed = line.trim();
    if (!trimmed || /^error\b/i.test(trimmed)) {
      return undefined;
    }

    const withFunction = trimmed.match(/^at\s+(.+?)\s+\(?(.+?):(\d+):(\d+)\)?$/);
    const withoutFunction = trimmed.match(/^at\s+(.+?):(\d+):(\d+)$/);
    const quickJsStyle = trimmed.match(/^(.*?)@(.+?):(\d+):(\d+)$/);
    let name = "AriadneTS";
    let modulePath = "";
    let lineNumber = 1;
    let columnNumber = 1;

    if (withFunction) {
      name = withFunction[1] || name;
      modulePath = withFunction[2];
      lineNumber = Number(withFunction[3]);
      columnNumber = Number(withFunction[4]);
    } else if (withoutFunction) {
      modulePath = withoutFunction[1];
      lineNumber = Number(withoutFunction[2]);
      columnNumber = Number(withoutFunction[3]);
    } else if (quickJsStyle) {
      name = quickJsStyle[1] || name;
      modulePath = quickJsStyle[2];
      lineNumber = Number(quickJsStyle[3]);
      columnNumber = Number(quickJsStyle[4]);
    } else {
      return undefined;
    }

    return {
      id,
      name: this.cleanStackFunctionName(name),
      source: {
        name: path.basename(modulePath),
        path: this.toSourcePath(modulePath),
      },
      line: Math.max(1, lineNumber || 1),
      column: Math.max(1, columnNumber || 1),
    };
  }

  cleanStackFunctionName(name) {
    const trimmed = String(name ?? "").trim();
    if (!trimmed || trimmed === "<anonymous>") {
      return "AriadneTS";
    }
    return trimmed.replace(/^async\s+/, "");
  }

  createStackFrame(status, id) {
    const modulePath = status?.module || "unknown.ts";
    return {
      id,
      name: status?.function || "AriadneTS",
      source: {
        name: path.basename(modulePath),
        path: this.toSourcePath(modulePath),
      },
      line: Math.max(1, Number(status?.line || 1)),
      column: Math.max(1, Number(status?.column || 0) + 1),
    };
  }

  scopes() {
    const variablesReference = this.storeVariablesReference({
      kind: "locals",
      values: null,
    });
    return {
      scopes: [
        {
          name: "Locals",
          variablesReference,
          expensive: false,
        },
      ],
    };
  }

  async variables(args) {
    const handle = this.variableHandles.get(Number(args.variablesReference));
    if (!handle) {
      return { variables: [] };
    }

    if (handle.kind === "locals" && handle.values === null) {
      const response = await this.client.command({ command: "variables" });
      handle.values = response.variables ?? {};
    }

    return {
      variables: Object.entries(handle.values ?? {}).map(([name, value]) =>
        this.toDebugVariable(name, value),
      ),
    };
  }

  async step(command) {
    await this.client.command({ command });
    this.wasPaused = false;
    return {};
  }

  storeVariablesReference(value) {
    const handle = this.nextVariableHandle++;
    this.variableHandles.set(handle, value);
    return handle;
  }

  toDebugVariable(name, value) {
    if (value !== null && typeof value === "object") {
      return {
        name,
        value: Array.isArray(value) ? `Array(${value.length})` : "Object",
        type: Array.isArray(value) ? "array" : "object",
        variablesReference: this.storeVariablesReference({
          kind: "object",
          values: value,
        }),
      };
    }
    return {
      name,
      value: value === undefined ? "undefined" : String(value),
      type: value === null ? "null" : typeof value,
      variablesReference: 0,
    };
  }

  toModulePath(sourcePath) {
    if (!sourcePath) {
      return "";
    }
    const absolute = path.resolve(sourcePath);
    let relative = path.relative(this.tsRoot, absolute);
    if (relative.startsWith("..")) {
      relative = sourcePath;
    }
    return relative.replaceAll(path.sep, "/");
  }

  toSourcePath(modulePath) {
    if (!modulePath || path.isAbsolute(modulePath)) {
      return modulePath;
    }
    return path.join(this.tsRoot, modulePath);
  }

  refreshProbeIndex() {
    this.probeLinesByModule = new Map();
    const distRoot = path.join(this.tsRoot, "dist");
    if (!fs.existsSync(distRoot)) {
      return;
    }
    for (const file of listFiles(distRoot)) {
      if (!file.endsWith(".js")) {
        continue;
      }
      const source = fs.readFileSync(file, "utf8");
      const pattern = /globalThis\.__ariadnets_debug_line\("([^"]+)",\s*(\d+),\s*\d+(?:,|\))/g;
      let match;
      while ((match = pattern.exec(source)) !== null) {
        const modulePath = match[1].replaceAll("\\", "/");
        const line = Number(match[2]);
        if (!this.probeLinesByModule.has(modulePath)) {
          this.probeLinesByModule.set(modulePath, new Set());
        }
        this.probeLinesByModule.get(modulePath).add(line);
      }
    }
  }

  resolveBreakpointLine(modulePath, requestedLine) {
    const probes = [...(this.probeLinesByModule.get(modulePath) ?? [])]
      .filter((line) => Number.isFinite(line))
      .sort((left, right) => left - right);
    if (probes.length === 0) {
      return requestedLine;
    }
    if (probes.includes(requestedLine)) {
      return requestedLine;
    }
    const next = probes.find((line) => line > requestedLine);
    if (next !== undefined) {
      return next;
    }
    return probes[probes.length - 1];
  }

  output(text) {
    this.dap.sendEvent("output", {
      category: "console",
      output: `[AriadneTS] ${text}\n`,
    });
  }
}

function listFiles(directory) {
  const results = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      results.push(...listFiles(fullPath));
    } else {
      results.push(fullPath);
    }
  }
  return results;
}

new AriadneDebugSession();
