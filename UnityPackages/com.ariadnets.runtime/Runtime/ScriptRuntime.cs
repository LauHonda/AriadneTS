using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AriadneTS.Runtime
{
    public sealed class ScriptRuntime : IDisposable
    {
        public const uint RequiredAbiVersion = 4;

        private static readonly NativeMethods.LogCallback LogCallback = HandleLog;
        private static readonly NativeMethods.ModuleLoadCallback ModuleLoadCallback = HandleModuleLoad;
        private static readonly NativeMethods.HostInvokeCallback HostInvokeCallback = HandleHostInvoke;

        private readonly Action<string> logHandler;
        private readonly Func<string, string> moduleLoader;
        private readonly Func<string, string, string> hostInvoker;
        private readonly int ownerThreadId;
        private GCHandle selfHandle;
        private IntPtr nativeRuntime;
        private Exception pendingHostException;
        private byte[] pendingHostResult;

        public ScriptRuntime(
            Action<string> logHandler,
            Func<string, string> moduleLoader = null,
            ulong memoryLimitBytes = 0,
            ulong maxStackSizeBytes = 1024 * 1024,
            uint executionTimeoutMilliseconds = 1000,
            Func<string, string, string> hostInvoker = null)
        {
            this.logHandler = logHandler ?? throw new ArgumentNullException(nameof(logHandler));
            this.moduleLoader = moduleLoader;
            this.hostInvoker = hostInvoker;
            ownerThreadId = Environment.CurrentManagedThreadId;

            var actualAbiVersion = NativeMethods.ts_runtime_abi_version();
            if (actualAbiVersion != RequiredAbiVersion)
            {
                throw new InvalidOperationException(
                    $"Native runtime ABI mismatch. Expected {RequiredAbiVersion}, got {actualAbiVersion}.");
            }

            selfHandle = GCHandle.Alloc(this);
            var config = new NativeMethods.RuntimeConfig
            {
                StructSize = (uint)Marshal.SizeOf<NativeMethods.RuntimeConfig>(),
                MemoryLimitBytes = memoryLimitBytes,
                LogCallback = LogCallback,
                LogUserData = GCHandle.ToIntPtr(selfHandle),
                ModuleLoadCallback = ModuleLoadCallback,
                ModuleLoadUserData = GCHandle.ToIntPtr(selfHandle),
                MaxStackSizeBytes = maxStackSizeBytes,
                ExecutionTimeoutMilliseconds = executionTimeoutMilliseconds,
                HostInvokeCallback = HostInvokeCallback,
                HostInvokeUserData = GCHandle.ToIntPtr(selfHandle),
            };

            nativeRuntime = NativeMethods.ts_runtime_create(ref config);
            if (nativeRuntime == IntPtr.Zero)
            {
                selfHandle.Free();
                throw new InvalidOperationException("Failed to create the native script runtime.");
            }
        }

        public void Evaluate(string source, string filename = "<eval>")
        {
            ThrowIfDisposed();
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var sourceBytes = Utf8NullTerminated(source);
            var filenameBytes = Utf8NullTerminated(filename ?? "<eval>");
            pendingHostException = null;
            var status = NativeMethods.ts_runtime_eval(
                nativeRuntime,
                sourceBytes,
                (UIntPtr)(sourceBytes.Length - 1),
                filenameBytes);

            ThrowPendingError(status);
        }

        public void EvaluateModule(string source, string moduleName)
        {
            ThrowIfDisposed();
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (moduleName == null)
            {
                throw new ArgumentNullException(nameof(moduleName));
            }

            var sourceBytes = Utf8NullTerminated(source);
            var moduleNameBytes = Utf8NullTerminated(moduleName);
            pendingHostException = null;
            var status = NativeMethods.ts_runtime_eval_module(
                nativeRuntime,
                sourceBytes,
                (UIntPtr)(sourceBytes.Length - 1),
                moduleNameBytes);

            ThrowPendingError(status);
        }

        public uint ExecutePendingJobs(uint maxJobs = 0)
        {
            ThrowIfDisposed();
            pendingHostException = null;
            var status = NativeMethods.ts_runtime_execute_pending_jobs(
                nativeRuntime,
                maxJobs,
                out var executedJobs);

            ThrowPendingError(status);
            return executedJobs;
        }

        public string Invoke(string method, string payloadJson = "null")
        {
            ThrowIfDisposed();
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            if (payloadJson == null)
            {
                throw new ArgumentNullException(nameof(payloadJson));
            }

            var methodBytes = Utf8NullTerminated(method);
            var payloadBytes = Utf8NullTerminated(payloadJson);
            pendingHostException = null;
            var status = NativeMethods.ts_runtime_invoke(
                nativeRuntime,
                methodBytes,
                (UIntPtr)(methodBytes.Length - 1),
                payloadBytes,
                (UIntPtr)(payloadBytes.Length - 1));

            ThrowPendingError(status);
            var pointer = NativeMethods.ts_runtime_last_result(nativeRuntime, out var length);
            return pointer == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUTF8(pointer, ToInt32(length));
        }

        public void Dispose()
        {
            ThrowIfWrongThread();
            if (nativeRuntime == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.ts_runtime_destroy(nativeRuntime);
            nativeRuntime = IntPtr.Zero;
            selfHandle.Free();
        }

        private static byte[] Utf8NullTerminated(string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            var terminated = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, terminated, 0, encoded.Length);
            return terminated;
        }

#if ENABLE_IL2CPP
        [AOT.MonoPInvokeCallback(typeof(NativeMethods.LogCallback))]
#endif
        private static void HandleLog(IntPtr userData, IntPtr message, UIntPtr messageLength)
        {
            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is ScriptRuntime runtime)
            {
                try
                {
                    runtime.logHandler(
                        Marshal.PtrToStringUTF8(message, ToInt32(messageLength)) ?? string.Empty);
                }
                catch (Exception exception)
                {
                    // Managed exceptions must never unwind through the native ABI.
                    runtime.pendingHostException = exception;
                }
            }
        }

        private void ThrowPendingError(NativeMethods.Status status)
        {
            if (pendingHostException != null)
            {
                throw new ScriptRuntimeException("HostCallbackError", pendingHostException.Message);
            }
            if (status != NativeMethods.Status.Ok)
            {
                throw new ScriptRuntimeException(status.ToString(), GetLastError());
            }
        }

#if ENABLE_IL2CPP
        [AOT.MonoPInvokeCallback(typeof(NativeMethods.ModuleLoadCallback))]
#endif
        private static NativeMethods.Status HandleModuleLoad(
            IntPtr userData,
            IntPtr moduleName,
            UIntPtr moduleNameLength,
            IntPtr sourceBuffer,
            UIntPtr sourceCapacity,
            out UIntPtr sourceLength)
        {
            sourceLength = UIntPtr.Zero;
            var handle = GCHandle.FromIntPtr(userData);
            if (!(handle.Target is ScriptRuntime runtime) || runtime.moduleLoader == null)
            {
                return NativeMethods.Status.ModuleNotFound;
            }

            try
            {
                var name = Marshal.PtrToStringUTF8(
                    moduleName,
                    ToInt32(moduleNameLength)) ?? string.Empty;
                var source = runtime.moduleLoader(name);
                if (source == null)
                {
                    return NativeMethods.Status.ModuleNotFound;
                }

                var bytes = Encoding.UTF8.GetBytes(source);
                sourceLength = (UIntPtr)bytes.Length;
                if (sourceBuffer == IntPtr.Zero)
                {
                    return NativeMethods.Status.Ok;
                }
                if (sourceCapacity.ToUInt64() < (ulong)bytes.Length)
                {
                    return NativeMethods.Status.BufferTooSmall;
                }

                Marshal.Copy(bytes, 0, sourceBuffer, bytes.Length);
                return NativeMethods.Status.Ok;
            }
            catch (Exception exception)
            {
                // Managed exceptions must never unwind through the native ABI.
                runtime.pendingHostException = exception;
                return NativeMethods.Status.HostError;
            }
        }

#if ENABLE_IL2CPP
        [AOT.MonoPInvokeCallback(typeof(NativeMethods.HostInvokeCallback))]
#endif
        private static NativeMethods.Status HandleHostInvoke(
            IntPtr userData,
            IntPtr method,
            UIntPtr methodLength,
            IntPtr payloadJson,
            UIntPtr payloadJsonLength,
            IntPtr resultBuffer,
            UIntPtr resultCapacity,
            out UIntPtr resultLength)
        {
            resultLength = UIntPtr.Zero;
            var handle = GCHandle.FromIntPtr(userData);
            if (!(handle.Target is ScriptRuntime runtime) || runtime.hostInvoker == null)
            {
                return NativeMethods.Status.HostError;
            }

            try
            {
                if (resultBuffer == IntPtr.Zero)
                {
                    var methodText = Marshal.PtrToStringUTF8(
                        method,
                        ToInt32(methodLength)) ?? string.Empty;
                    var payloadText = Marshal.PtrToStringUTF8(
                        payloadJson,
                        ToInt32(payloadJsonLength)) ?? "null";
                    var resultText = runtime.hostInvoker(methodText, payloadText) ?? "null";
                    runtime.pendingHostResult = Encoding.UTF8.GetBytes(resultText);
                }

                var bytes = runtime.pendingHostResult ?? Encoding.UTF8.GetBytes("null");
                resultLength = (UIntPtr)bytes.Length;
                if (resultBuffer == IntPtr.Zero)
                {
                    return NativeMethods.Status.Ok;
                }
                if (resultCapacity.ToUInt64() < (ulong)bytes.Length)
                {
                    return NativeMethods.Status.BufferTooSmall;
                }

                Marshal.Copy(bytes, 0, resultBuffer, bytes.Length);
                runtime.pendingHostResult = null;
                return NativeMethods.Status.Ok;
            }
            catch (Exception exception)
            {
                runtime.pendingHostResult = null;
                runtime.pendingHostException = exception;
                return NativeMethods.Status.HostError;
            }
        }

        private string GetLastError()
        {
            var pointer = NativeMethods.ts_runtime_last_error(nativeRuntime, out var length);
            return pointer == IntPtr.Zero
                ? "Unknown script runtime error."
                : Marshal.PtrToStringUTF8(pointer, ToInt32(length)) ?? "Unknown script runtime error.";
        }

        private static int ToInt32(UIntPtr value)
        {
            return checked((int)value.ToUInt64());
        }

        private void ThrowIfDisposed()
        {
            ThrowIfWrongThread();
            if (nativeRuntime == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(ScriptRuntime));
            }
        }

        private void ThrowIfWrongThread()
        {
            if (Environment.CurrentManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "ScriptRuntime must be used and disposed on the thread that created it.");
            }
        }
    }
}
