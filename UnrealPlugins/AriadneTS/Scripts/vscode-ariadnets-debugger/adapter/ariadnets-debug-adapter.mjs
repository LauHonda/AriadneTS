#!/usr/bin/env node
import fs from "node:fs";
import net from "node:net";
import path from "node:path";

class DapConnection {
  constructor(input, output, trace) {
    this.input = input;
    this.output = output;
    this.buffer = Buffer.alloc(0);
    this.nextSeq = 1;
    this.handlers = new Map();
    this.trace = trace;

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
      this.trace?.("dap.receive", summarizeDapMessage(message));
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
    this.trace?.("dap.send", summarizeDapMessage(message));
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
  constructor(trace) {
    this.host = "127.0.0.1";
    this.port = 9229;
    this.timeoutMs = 750;
    this.trace = trace;
  }

  configure(options) {
    this.host = options.host ?? this.host;
    this.port = Number(options.port ?? this.port);
  }

  async command(payload) {
    this.trace?.("runtime.send", { command: payload.command });
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
          const parsed = JSON.parse(text);
          this.trace?.("runtime.receive", summarizeRuntimeResponse(payload.command, parsed));
          resolve(parsed);
        } catch {
          this.trace?.("runtime.receive", { command: payload.command, text: true });
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
        if (!sawGreeting && response.includes("Commands:")) {
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
    this.tracePath = undefined;
    this.traceInitialized = false;
    this.trace = (event, data) => this.writeTrace(event, data);
    this.dap = new DapConnection(process.stdin, process.stdout, this.trace);
    this.client = new AriadneTsClient(this.trace);
    this.tsRoot = path.resolve("TypeScript");
    this.pollIntervalMs = 250;
    this.pollTimer = undefined;
    this.pollInProgress = false;
    this.pendingBreakpoints = new Map();
    this.breakpointsDirty = true;
    this.probeLinesByModule = new Map();
    this.debugMetadata = undefined;
    this.sourceMapCache = new Map();
    this.variableHandles = new Map();
    this.nextVariableHandle = 1;
    this.lastStatus = { state: "running", module: "", line: 0, column: 0 };
    this.wasPaused = false;
    this.lastStoppedPauseId = 0;
    this.wasRuntimeConnected = false;
    this.threadId = 1;
    this.nextStopReason = "breakpoint";

    this.registerHandlers();
  }

  registerHandlers() {
    this.dap.on("initialize", async () => {
      setImmediate(() => this.dap.sendEvent("initialized"));
      return {
        supportsConfigurationDoneRequest: true,
        supportsTerminateRequest: true,
        supportsStepInRequest: true,
        supportsStepOutRequest: true,
        supportsNextRequest: true,
        supportsSetVariable: false,
        supportsEvaluateForHovers: true,
        supportsStepBack: false,
        supportsInvalidatedEvent: true,
        exceptionBreakpointFilters: [],
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
    this.dap.on("setExceptionBreakpoints", async () => ({ breakpoints: [] }));
    this.dap.on("setFunctionBreakpoints", async () => ({ breakpoints: [] }));
    this.dap.on("threads", async () => ({
      threads: [{ id: this.threadId, name: "AriadneTS Runtime" }],
    }));
    this.dap.on("stackTrace", async () => {
      this.writeTrace("debug.request", { command: "stackTrace" });
      return await this.stackTrace();
    });
    this.dap.on("scopes", async (args) => {
      this.writeTrace("debug.request", { command: "scopes", frameId: args.frameId });
      return this.scopes();
    });
    this.dap.on("variables", async (args) => {
      this.writeTrace("debug.request", {
        command: "variables",
        variablesReference: args.variablesReference,
      });
      return await this.variables(args);
    });
    this.dap.on("evaluate", async (args) => this.evaluate(args));
    this.dap.on("continue", async () => this.resume("continue"));
    this.dap.on("next", async () => this.resume("next"));
    this.dap.on("stepIn", async () => this.resume("stepIn"));
    this.dap.on("stepOut", async () => this.resume("stepOut"));
    this.dap.on("disconnect", async () => {
      await this.endDebugSession();
      return {};
    });
    this.dap.on("terminate", async () => {
      await this.endDebugSession();
      this.dap.sendEvent("terminated");
      return {};
    });
  }

  configure(args) {
    this.client.configure(args);
    if (args.tsRoot) {
      this.tsRoot = path.resolve(args.tsRoot);
    }
    this.tracePath = args.tracePath ? path.resolve(args.tracePath) : undefined;
    if (this.tracePath && !this.traceInitialized) {
      try {
        fs.mkdirSync(path.dirname(this.tracePath), { recursive: true });
        fs.writeFileSync(this.tracePath, "", "utf8");
      } catch {
        // Tracing must never break the debug session.
      }
      this.traceInitialized = true;
    }
    this.writeTrace("session.configure", {
      host: this.client.host,
      port: this.client.port,
      tsRoot: this.tsRoot,
    });
    this.refreshProbeIndex();
    if (!this.debugMetadata) {
      this.output(
        `Debug metadata was not found under ${this.tsRoot}. ` +
        "Build the script package or verify the launch configuration tsRoot.",
      );
    }
    this.sourceMapCache.clear();
    this.breakpointsDirty = true;
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
    this.breakpointsDirty = true;

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
    if (!this.breakpointsDirty) {
      return true;
    }

    for (const [modulePath, lines] of this.pendingBreakpoints) {
      if (!await this.applyBreakpointsBestEffort(modulePath, lines)) {
        return false;
      }
    }
    this.breakpointsDirty = false;
    return true;
  }

  async applyBreakpointsBestEffort(modulePath, lines) {
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
      if (lines.length > 0) {
        this.output(`Applied ${lines.length} breakpoint(s) to ${modulePath} on ${this.client.host}:${this.client.port}.`);
      }
      this.breakpointsDirty = false;
      return true;
    } catch (error) {
      this.breakpointsDirty = true;
      // The Unity/Unreal runtime may not be in Play Mode yet. Polling will retry.
      return false;
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

  async endDebugSession() {
    this.stopPolling();
    await this.resumeRuntimeBestEffort();
    this.wasPaused = false;
    this.wasRuntimeConnected = false;
    this.lastStoppedPauseId = 0;
    this.lastStatus = { state: "running", module: "", line: 0, column: 0 };
    this.variableHandles.clear();
    this.nextVariableHandle = 1;
  }

  async resumeRuntimeBestEffort() {
    try {
      await this.client.command({ command: "continue" });
    } catch {
      // The runtime may already have stopped with Unity/Unreal Play Mode.
    }
  }

  async pollStatus() {
    if (this.pollInProgress) {
      return;
    }
    this.pollInProgress = true;
    try {
      await this.applyAllBreakpointsBestEffort();
      const status = await this.client.command({ command: "status" });
      if (!this.wasRuntimeConnected) {
        this.output(`Connected to AriadneTS runtime at ${this.client.host}:${this.client.port}.`);
      }
      this.wasRuntimeConnected = true;
      this.lastStatus = status;
      const isPaused = status.state === "paused";
      const pauseId = Number(status.pauseId ?? 0);
      const isNewPause = pauseId > 0
        ? pauseId !== this.lastStoppedPauseId
        : !this.wasPaused;
      if (isPaused && isNewPause) {
        this.wasPaused = true;
        if (pauseId > 0) {
          this.lastStoppedPauseId = pauseId;
        }
        this.variableHandles.clear();
        this.nextVariableHandle = 1;
        const reason = this.nextStopReason;
        this.nextStopReason = "breakpoint";
        this.dap.sendEvent("stopped", {
          reason,
          description: this.describePausedStatus(status),
          text: this.describePausedStatus(status),
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
      this.wasPaused = false;
      this.lastStoppedPauseId = 0;
      this.lastStatus = { state: "running", module: "", line: 0, column: 0 };
      this.variableHandles.clear();
      this.nextVariableHandle = 1;
      this.breakpointsDirty = true;
      // Runtime not available yet. Keep polling quietly.
    } finally {
      this.pollInProgress = false;
    }
  }

  async stackTrace() {
    const status = this.lastStatus?.state === "paused"
      ? this.lastStatus
      : await this.safeStatus();
    const runtimeStack = await this.safeStack(status);
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

  describePausedStatus(status) {
    const modulePath = status?.module || "unknown.ts";
    const line = Math.max(1, Number(status?.line || 1));
    const name = status?.function || "AriadneTS";
    return `${name} at ${modulePath}:${line}`;
  }

  async safeStatus() {
    try {
      return await this.client.command({ command: "status" });
    } catch {
      return this.lastStatus;
    }
  }

  async safeStack(status) {
    if (status?.state !== "paused") {
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

    const mapped = this.mapGeneratedLocationToSource(modulePath, lineNumber, columnNumber);
    return {
      id,
      name: this.cleanStackFunctionName(name),
      source: {
        name: path.basename(mapped.path),
        path: mapped.path,
      },
      line: mapped.line,
      column: mapped.column,
    };
  }

  mapGeneratedLocationToSource(modulePath, lineNumber, columnNumber) {
    const fallback = {
      path: this.toSourcePath(modulePath),
      line: Math.max(1, lineNumber || 1),
      column: Math.max(1, columnNumber || 1),
    };
    if (!modulePath.endsWith(".js")) {
      return fallback;
    }

    const generatedPath = path.isAbsolute(modulePath)
      ? modulePath
      : path.join(this.tsRoot, "dist", modulePath);
    const sourceMap = this.loadSourceMap(generatedPath);
    if (!sourceMap) {
      return fallback;
    }

    const mapping = findSourceMapMapping(
      sourceMap,
      Math.max(1, lineNumber || 1),
      Math.max(0, (columnNumber || 1) - 1),
    );
    if (!mapping) {
      return fallback;
    }

    const source = sourceMap.sources[mapping.sourceIndex];
    if (!source) {
      return fallback;
    }

    const sourceRoot = sourceMap.sourceRoot || "";
    return {
      path: path.resolve(path.dirname(generatedPath), sourceRoot, source),
      line: mapping.originalLine + 1,
      column: mapping.originalColumn + 1,
    };
  }

  loadSourceMap(generatedPath) {
    const mapPath = `${generatedPath}.map`;
    if (this.sourceMapCache.has(mapPath)) {
      return this.sourceMapCache.get(mapPath);
    }
    if (!fs.existsSync(mapPath)) {
      this.sourceMapCache.set(mapPath, undefined);
      return undefined;
    }
    try {
      const parsed = JSON.parse(fs.readFileSync(mapPath, "utf8"));
      if (!Array.isArray(parsed.sources) || typeof parsed.mappings !== "string") {
        this.sourceMapCache.set(mapPath, undefined);
        return undefined;
      }
      this.sourceMapCache.set(mapPath, parsed);
      return parsed;
    } catch {
      this.sourceMapCache.set(mapPath, undefined);
      return undefined;
    }
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
    const localsReference = this.storeVariablesReference({
      kind: "locals",
      values: null,
    });
    const runtimeReference = this.storeVariablesReference({
      kind: "runtime",
      values: this.runtimeScopeValues(),
    });
    return {
      scopes: [
        {
          name: "Locals",
          variablesReference: localsReference,
          expensive: false,
        },
        {
          name: "AriadneTS Runtime",
          variablesReference: runtimeReference,
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
      try {
        const response = await this.client.command({ command: "variables" });
        handle.values = response.variables ?? {};
      } catch (error) {
        handle.values = {
          error: error instanceof Error ? error.message : String(error),
        };
      }
    }

    return {
      variables: Object.entries(handle.values ?? {}).map(([name, value]) =>
        this.toDebugVariable(name, value),
      ),
    };
  }

  async evaluate(args) {
    const expression = String(args.expression ?? "").trim();
    if (!expression) {
      return this.toEvaluationResult(undefined);
    }

    if (this.lastStatus?.state !== "paused") {
      throw new Error("AriadneTS is not paused.");
    }

    const locals = await this.currentLocals();
    const runtime = this.runtimeScopeValues();
    const resolution = this.resolveDebugExpression(expression, locals, runtime);
    if (!resolution.found) {
      throw new Error(`Unsupported or unavailable expression: ${expression}`);
    }
    return this.toEvaluationResult(resolution.value);
  }

  async currentLocals() {
    try {
      const response = await this.client.command({ command: "variables" });
      return response.variables ?? {};
    } catch {
      return {};
    }
  }

  resolveDebugExpression(expression, locals, runtime) {
    if (expression === "$runtime") {
      return { found: true, value: runtime };
    }
    if (Object.hasOwn(locals, expression)) {
      return { found: true, value: locals[expression] };
    }
    if (Object.hasOwn(runtime, expression)) {
      return { found: true, value: runtime[expression] };
    }

    const pathParts = this.parseDebugExpressionPath(expression);
    if (pathParts.length === 0) {
      return { found: false, value: undefined };
    }

    let value;
    let found = false;
    const root = pathParts[0];
    if (root === "$runtime") {
      value = runtime;
      found = true;
    } else if (Object.hasOwn(locals, root)) {
      value = locals[root];
      found = true;
    } else if (Object.hasOwn(runtime, root)) {
      value = runtime[root];
      found = true;
    }
    if (!found) {
      return { found: false, value: undefined };
    }

    for (const part of pathParts.slice(1)) {
      if (value === null || value === undefined) {
        return { found: false, value: undefined };
      }
      if (typeof value !== "object") {
        return { found: false, value: undefined };
      }
      if (!Object.hasOwn(value, part)) {
        return { found: false, value: undefined };
      }
      value = value[part];
    }
    return { found: true, value };
  }

  parseDebugExpressionPath(expression) {
    const parts = [];
    let index = 0;
    const readIdentifier = () => {
      const match = expression.slice(index).match(/^[$A-Za-z_][$\w]*/);
      if (!match) {
        return undefined;
      }
      index += match[0].length;
      return match[0];
    };

    const first = readIdentifier();
    if (!first) {
      return [];
    }
    parts.push(first);

    while (index < expression.length) {
      if (expression[index] === ".") {
        ++index;
        const name = readIdentifier();
        if (!name) {
          return [];
        }
        parts.push(name);
        continue;
      }

      if (expression[index] !== "[") {
        return [];
      }
      ++index;
      const close = expression.indexOf("]", index);
      if (close < 0) {
        return [];
      }
      const key = expression.slice(index, close).trim();
      if (/^\d+$/.test(key)) {
        parts.push(key);
      } else {
        const stringKey = key.match(/^["'](.+)["']$/);
        if (!stringKey) {
          return [];
        }
        parts.push(stringKey[1]);
      }
      index = close + 1;
    }
    return parts;
  }

  toEvaluationResult(value) {
    const variable = this.toDebugVariable("result", value);
    return {
      result: variable.value,
      type: variable.type,
      variablesReference: variable.variablesReference,
    };
  }

  runtimeScopeValues() {
    const status = this.lastStatus ?? {};
    return {
      state: status.state ?? "unknown",
      pauseId: Number(status.pauseId ?? 0),
      module: status.module ?? "",
      function: status.function ?? "",
      line: Number(status.line ?? 0),
      column: Number(status.column ?? 0),
      endpoint: `${this.client.host}:${this.client.port}`,
      tsRoot: this.tsRoot,
    };
  }

  async resume(command) {
    await this.client.command({ command });
    this.wasPaused = false;
    this.nextStopReason = command === "continue" ? "breakpoint" : "step";
    this.variableHandles.clear();
    this.nextVariableHandle = 1;
    this.dap.sendEvent("continued", {
      threadId: this.threadId,
      allThreadsContinued: true,
    });
    return command === "continue"
      ? { allThreadsContinued: true }
      : {};
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
    this.debugMetadata = this.loadDebugMetadata();
    if (this.debugMetadata) {
      for (const probe of this.debugMetadata.Probes ?? []) {
        const modulePath = String(probe.Source ?? "").replaceAll("\\", "/");
        const line = Number(probe.Line);
        if (!modulePath || !Number.isFinite(line)) {
          continue;
        }
        if (!this.probeLinesByModule.has(modulePath)) {
          this.probeLinesByModule.set(modulePath, new Set());
        }
        this.probeLinesByModule.get(modulePath).add(line);
      }
      return;
    }

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

  loadDebugMetadata() {
    const candidates = [
      path.join(this.tsRoot, ".ariadnets", "debug-metadata.json"),
      path.join(this.tsRoot, "dist", "debug-metadata.json"),
    ];
    for (const metadataPath of candidates) {
      if (!fs.existsSync(metadataPath)) {
        continue;
      }
      try {
        const metadata = JSON.parse(fs.readFileSync(metadataPath, "utf8"));
        if (metadata?.SchemaVersion === 1 && Array.isArray(metadata.Probes)) {
          return metadata;
        }
      } catch {
        // Try the next compatible metadata location.
      }
    }
    return undefined;
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

  writeTrace(event, data) {
    if (!this.tracePath) {
      return;
    }
    try {
      fs.mkdirSync(path.dirname(this.tracePath), { recursive: true });
      fs.appendFileSync(
        this.tracePath,
        `${new Date().toISOString()} ${event} ${JSON.stringify(data ?? {})}\n`,
        "utf8",
      );
    } catch {
      // Tracing must never break the debug session.
    }
  }
}

function summarizeDapMessage(message) {
  const summary = {
    type: message?.type,
    command: message?.command,
    event: message?.event,
    success: message?.success,
    requestSeq: message?.request_seq,
  };
  if (message?.command === "scopes") {
    summary.frameId = message.arguments?.frameId;
  } else if (message?.command === "variables") {
    summary.variablesReference = message.arguments?.variablesReference;
  } else if (message?.command === "stackTrace") {
    summary.threadId = message.arguments?.threadId;
  }
  return summary;
}

function summarizeRuntimeResponse(command, response) {
  const summary = { command };
  if (command === "status") {
    summary.state = response?.state;
    summary.pauseId = response?.pauseId;
    summary.module = response?.module;
    summary.line = response?.line;
  } else if (command === "variables") {
    summary.variableNames = Object.keys(response?.variables ?? {});
  } else if (command === "stack") {
    summary.hasStack = typeof response?.stack === "string" && response.stack.length > 0;
  } else {
    summary.ok = response?.ok;
  }
  return summary;
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

function findSourceMapMapping(sourceMap, generatedLine, generatedColumn) {
  const lines = sourceMap.mappings.split(";");
  const targetLineIndex = generatedLine - 1;
  if (targetLineIndex < 0 || targetLineIndex >= lines.length) {
    return undefined;
  }

  let sourceIndex = 0;
  let originalLine = 0;
  let originalColumn = 0;
  let nameIndex = 0;

  for (let lineIndex = 0; lineIndex <= targetLineIndex; ++lineIndex) {
    let generatedColumnCursor = 0;
    let bestMapping;
    const segments = lines[lineIndex].split(",");
    for (const segment of segments) {
      if (!segment) {
        continue;
      }
      const values = decodeSourceMapSegment(segment);
      if (values.length === 0) {
        continue;
      }
      generatedColumnCursor += values[0];
      if (values.length >= 4) {
        sourceIndex += values[1];
        originalLine += values[2];
        originalColumn += values[3];
        if (values.length >= 5) {
          nameIndex += values[4];
        }
        if (lineIndex === targetLineIndex && generatedColumnCursor <= generatedColumn) {
          bestMapping = {
            generatedColumn: generatedColumnCursor,
            sourceIndex,
            originalLine,
            originalColumn,
            nameIndex,
          };
        }
      }
    }
    if (lineIndex === targetLineIndex) {
      return bestMapping;
    }
  }
  return undefined;
}

function decodeSourceMapSegment(segment) {
  const values = [];
  let index = 0;
  while (index < segment.length) {
    const decoded = decodeVlq(segment, index);
    values.push(decoded.value);
    index = decoded.nextIndex;
  }
  return values;
}

function decodeVlq(text, startIndex) {
  let result = 0;
  let shift = 0;
  let index = startIndex;
  while (index < text.length) {
    const digit = sourceMapBase64Value(text[index]);
    ++index;
    result += (digit & 31) << shift;
    if ((digit & 32) === 0) {
      break;
    }
    shift += 5;
  }

  const negative = (result & 1) === 1;
  const value = result >> 1;
  return {
    value: negative ? -value : value,
    nextIndex: index,
  };
}

function sourceMapBase64Value(character) {
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
  return 0;
}

new AriadneDebugSession();
