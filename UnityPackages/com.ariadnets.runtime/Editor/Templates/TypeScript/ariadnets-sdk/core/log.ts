export type LogLevel = "log" | "warning" | "error";

export interface LogEntry {
  readonly level: LogLevel;
  readonly message: unknown;
}

export interface Logger {
  readonly log: (message: unknown) => void;
  readonly warning: (message: unknown) => void;
  readonly warn: (message: unknown) => void;
  readonly error: (message: unknown) => void;
}

function writeLog(entry: LogEntry): void {
  const formattedMessage = formatMessage(entry.message);
  try {
    host.invoke("ariadnets.log", {
      level: entry.level,
      message: formattedMessage,
    });
  } catch {
    host.log(`[${entry.level}] ${formattedMessage}`);
  }
}

function formatMessage(message: unknown): string {
  if (typeof message === "string") {
    return message;
  }
  if (message instanceof Error) {
    return `${message.name}: ${message.message}\n${message.stack ?? ""}`.trim();
  }
  try {
    return JSON.stringify(message);
  } catch {
    return String(message);
  }
}

const loggerImplementation: Logger = Object.freeze({
  log(message: unknown): void {
    writeLog({ level: "log", message });
  },

  warning(message: unknown): void {
    writeLog({ level: "warning", message });
  },

  warn(message: unknown): void {
    writeLog({ level: "warning", message });
  },

  error(message: unknown): void {
    writeLog({ level: "error", message });
  },
});

export const logger: Logger = loggerImplementation;
export const log = loggerImplementation.log;
export const warning = loggerImplementation.warning;
export const warn = loggerImplementation.warn;
export const error = loggerImplementation.error;
