export function invokeHost<TResult = unknown>(
  method: string,
  payload: unknown = null,
): TResult {
  return host.invoke(method, payload) as TResult;
}
