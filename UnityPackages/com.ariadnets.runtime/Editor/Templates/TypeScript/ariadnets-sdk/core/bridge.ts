export interface BridgeError {
  readonly code: string;
  readonly message: string;
  readonly details?: unknown;
}

export interface BridgeResult<TResult> {
  readonly ok: boolean;
  readonly result?: TResult;
  readonly error?: BridgeError;
}

export interface AsyncBeginResult {
  readonly requestId: number;
}

export interface AsyncBridgeRequest {
  readonly requestId: number;
  readonly method: string;
  readonly params: unknown;
}

export interface AsyncBridgeProgress {
  readonly requestId: number;
  readonly downloadedBytes?: number;
  readonly totalBytes?: number;
  readonly percent: number;
}

export interface AsyncBridgeCompletion<TResult = unknown> {
  readonly requestId: number;
  readonly ok: boolean;
  readonly result?: TResult;
  readonly error?: BridgeError;
}

export interface AsyncBridgeCallbacks<TResult> {
  readonly onComplete?: (result: TResult) => void;
  readonly onError?: (error: unknown) => void;
  readonly onProgress?: (progress: AsyncBridgeProgress) => void;
}

export class BridgeCallError extends Error {
  readonly code: string;
  readonly details?: unknown;

  constructor(error: BridgeError) {
    super(error.message);
    this.name = "BridgeCallError";
    this.code = error.code;
    this.details = error.details;
  }
}

let nextRequestId = 1;
const pendingAsyncRequests = new Map<number, AsyncBridgeCallbacks<unknown>>();

export function callSync<TResult = unknown>(
  method: string,
  params: unknown = null,
): TResult {
  const response = host.invoke(method, params) as BridgeResult<TResult> | TResult;
  if (isBridgeResult<TResult>(response)) {
    if (response.ok) {
      return response.result as TResult;
    }
    throw new BridgeCallError(response.error ?? {
      code: "BridgeError",
      message: `Bridge call failed: ${method}`,
    });
  }
  return response as TResult;
}

export async function callAsync<TResult = unknown>(
  method: string,
  params: unknown = null,
): Promise<TResult> {
  return await Promise.resolve().then(() => callSync<TResult>(method, params));
}

export function beginAsync<TResult = unknown>(
  method: string,
  params: unknown,
  callbacks: AsyncBridgeCallbacks<TResult> = {},
): number {
  const requestId = nextRequestId++;
  pendingAsyncRequests.set(
    requestId,
    callbacks as AsyncBridgeCallbacks<unknown>);
  try {
    callSync<AsyncBeginResult>("ariadnets.async.begin", {
      requestId,
      method,
      params,
    } satisfies AsyncBridgeRequest);
  } catch (error) {
    pendingAsyncRequests.delete(requestId);
    callbacks.onError?.(error);
  }
  return requestId;
}

export function handleAsyncProgress(progress: AsyncBridgeProgress): void {
  pendingAsyncRequests.get(progress.requestId)?.onProgress?.(progress);
}

export function handleAsyncCompletion<TResult = unknown>(
  completion: AsyncBridgeCompletion<TResult>,
): void {
  const callbacks = pendingAsyncRequests.get(completion.requestId) as
    | AsyncBridgeCallbacks<TResult>
    | undefined;
  if (!callbacks) {
    return;
  }

  pendingAsyncRequests.delete(completion.requestId);
  if (completion.ok) {
    callbacks.onComplete?.(completion.result as TResult);
  } else {
    callbacks.onError?.(completion.error ?? {
      code: "BridgeError",
      message: `Async bridge request failed: ${completion.requestId}`,
    });
  }
}

function isBridgeResult<TResult>(value: unknown): value is BridgeResult<TResult> {
  if (value === null || typeof value !== "object") {
    return false;
  }
  return "ok" in value;
}
