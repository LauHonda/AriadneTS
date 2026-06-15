using System;
using System.Runtime.InteropServices;

namespace AriadneTS.Runtime
{
    internal static class NativeMethods
    {
#if UNITY_IOS && !UNITY_EDITOR
        internal const string LibraryName = "__Internal";
#else
        internal const string LibraryName = "ariadnets";
#endif

        internal enum Status
        {
            Ok = 0,
            InvalidArgument = 1,
            OutOfMemory = 2,
            ScriptError = 3,
            InternalError = 4,
            ModuleNotFound = 5,
            BufferTooSmall = 6,
            HostError = 7,
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void LogCallback(
            IntPtr userData,
            IntPtr message,
            UIntPtr messageLength);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate Status ModuleLoadCallback(
            IntPtr userData,
            IntPtr moduleName,
            UIntPtr moduleNameLength,
            IntPtr sourceBuffer,
            UIntPtr sourceCapacity,
            out UIntPtr sourceLength);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RuntimeConfig
        {
            internal uint StructSize;
            internal ulong MemoryLimitBytes;
            internal LogCallback LogCallback;
            internal IntPtr LogUserData;
            internal ModuleLoadCallback ModuleLoadCallback;
            internal IntPtr ModuleLoadUserData;
            internal ulong MaxStackSizeBytes;
            internal uint ExecutionTimeoutMilliseconds;
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint ts_runtime_abi_version();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ts_runtime_create(ref RuntimeConfig config);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ts_runtime_destroy(IntPtr runtime);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern Status ts_runtime_eval(
            IntPtr runtime,
            byte[] source,
            UIntPtr sourceLength,
            byte[] filename);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern Status ts_runtime_eval_module(
            IntPtr runtime,
            byte[] source,
            UIntPtr sourceLength,
            byte[] moduleName);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern Status ts_runtime_execute_pending_jobs(
            IntPtr runtime,
            uint maxJobs,
            out uint executedJobs);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern Status ts_runtime_invoke(
            IntPtr runtime,
            byte[] method,
            UIntPtr methodLength,
            byte[] payloadJson,
            UIntPtr payloadJsonLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ts_runtime_last_result(
            IntPtr runtime,
            out UIntPtr resultLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ts_runtime_last_error(
            IntPtr runtime,
            out UIntPtr errorLength);
    }
}
