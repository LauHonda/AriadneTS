#include "AriadneNativeRuntime.h"

namespace
{
FString Utf8ToString(const char* Data, size_t Length)
{
    if (!Data || Length == 0)
    {
        return FString();
    }
    FUTF8ToTCHAR Converter(Data, Length);
    return FString(Converter.Length(), Converter.Get());
}
}

FAriadneNativeRuntime::FAriadneNativeRuntime() = default;

FAriadneNativeRuntime::~FAriadneNativeRuntime()
{
    Destroy();
}

bool FAriadneNativeRuntime::Create(
    FAriadneLogDelegate InLog,
    FAriadneModuleLoadDelegate InModuleLoader,
    FAriadneHostInvokeDelegate InHostInvoker,
    uint64 MemoryLimitBytes,
    uint64 MaxStackSizeBytes,
    uint32 ExecutionTimeoutMilliseconds,
    bool bDebugEnabled,
    uint32 DebugProtocol,
    const FString& DebugHost,
    uint16 DebugPort,
    bool bDebugWaitForAttach,
    FString& OutError)
{
    Destroy();

    const uint32 ActualAbi = ts_runtime_abi_version();
    if (ActualAbi != RequiredAbiVersion)
    {
        OutError = FString::Printf(TEXT("Native runtime ABI mismatch. Expected %u, got %u."), RequiredAbiVersion, ActualAbi);
        return false;
    }

    Log = InLog;
    ModuleLoader = InModuleLoader;
    HostInvoker = InHostInvoker;

    const FString EffectiveDebugHost = DebugHost.IsEmpty() ? FString(TEXT("127.0.0.1")) : DebugHost;
    FTCHARToUTF8 DebugHostUtf8(*EffectiveDebugHost);

    ts_runtime_config Config = {};
    Config.struct_size = sizeof(ts_runtime_config);
    Config.memory_limit_bytes = MemoryLimitBytes;
    Config.log_callback = &FAriadneNativeRuntime::HandleLog;
    Config.log_user_data = this;
    Config.module_load_callback = &FAriadneNativeRuntime::HandleModuleLoad;
    Config.module_load_user_data = this;
    Config.max_stack_size_bytes = MaxStackSizeBytes;
    Config.execution_timeout_milliseconds = ExecutionTimeoutMilliseconds;
    Config.host_invoke_callback = &FAriadneNativeRuntime::HandleHostInvoke;
    Config.host_invoke_user_data = this;
    Config.debug_enabled = bDebugEnabled ? 1u : 0u;
    Config.debug_protocol = DebugProtocol;
    Config.debug_host = DebugHostUtf8.Get();
    Config.debug_port = DebugPort;
    Config.debug_wait_for_attach = bDebugWaitForAttach ? 1u : 0u;

    Runtime = ts_runtime_create(&Config);
    if (!Runtime)
    {
        OutError = TEXT("Failed to create AriadneTS native runtime.");
        return false;
    }
    return true;
}

void FAriadneNativeRuntime::Destroy()
{
    if (Runtime)
    {
        ts_runtime_destroy(Runtime);
        Runtime = nullptr;
    }
    PinnedUtf8.Empty();
    PendingHostResult.Empty();
}

bool FAriadneNativeRuntime::EvaluateModule(const FString& Source, const FString& ModuleName, FString& OutError)
{
    if (!Runtime)
    {
        OutError = TEXT("AriadneTS runtime is not created.");
        return false;
    }

    FTCHARToUTF8 SourceUtf8(*Source);
    FTCHARToUTF8 ModuleUtf8(*ModuleName);
    const ts_status Status = ts_runtime_eval_module(
        Runtime,
        SourceUtf8.Get(),
        SourceUtf8.Length(),
        ModuleUtf8.Get());
    if (Status != TS_STATUS_OK)
    {
        OutError = LastError();
        return false;
    }
    return true;
}

bool FAriadneNativeRuntime::Invoke(
    const FString& Method,
    const FString& PayloadJson,
    FString& OutResult,
    FString& OutError)
{
    if (!Runtime)
    {
        OutError = TEXT("AriadneTS runtime is not created.");
        return false;
    }

    FTCHARToUTF8 MethodUtf8(*Method);
    FTCHARToUTF8 PayloadUtf8(*PayloadJson);
    const ts_status Status = ts_runtime_invoke(
        Runtime,
        MethodUtf8.Get(),
        MethodUtf8.Length(),
        PayloadUtf8.Get(),
        PayloadUtf8.Length());
    if (Status != TS_STATUS_OK)
    {
        OutError = LastError();
        return false;
    }

    size_t ResultLength = 0;
    const char* Result = ts_runtime_last_result(Runtime, &ResultLength);
    OutResult = Utf8ToString(Result, ResultLength);
    return true;
}

bool FAriadneNativeRuntime::ExecutePendingJobs(uint32 MaxJobs, uint32& OutExecutedJobs, FString& OutError)
{
    OutExecutedJobs = 0;
    if (!Runtime)
    {
        return true;
    }

    const ts_status Status = ts_runtime_execute_pending_jobs(Runtime, MaxJobs, &OutExecutedJobs);
    if (Status != TS_STATUS_OK)
    {
        OutError = LastError();
        return false;
    }
    return true;
}

void FAriadneNativeRuntime::HandleLog(void* UserData, const char* Message, size_t MessageLength)
{
    auto* Self = static_cast<FAriadneNativeRuntime*>(UserData);
    if (!Self || !Self->Log.IsBound())
    {
        return;
    }

    Self->Log.Execute(Utf8ToString(Message, MessageLength));
}

ts_status FAriadneNativeRuntime::HandleModuleLoad(
    void* UserData,
    const char* ModuleName,
    size_t ModuleNameLength,
    char* SourceBuffer,
    size_t SourceCapacity,
    size_t* SourceLength)
{
    auto* Self = static_cast<FAriadneNativeRuntime*>(UserData);
    if (!Self || !Self->ModuleLoader.IsBound() || !SourceLength)
    {
        return TS_STATUS_MODULE_NOT_FOUND;
    }

    const FString ModuleNameText = Utf8ToString(ModuleName, ModuleNameLength);
    const FString Source = Self->ModuleLoader.Execute(ModuleNameText);
    if (Source.IsEmpty())
    {
        *SourceLength = 0;
        return TS_STATUS_MODULE_NOT_FOUND;
    }

    FTCHARToUTF8 SourceUtf8(*Source);
    *SourceLength = static_cast<size_t>(SourceUtf8.Length());
    if (!SourceBuffer || SourceCapacity < *SourceLength)
    {
        return TS_STATUS_BUFFER_TOO_SMALL;
    }

    FMemory::Memcpy(SourceBuffer, SourceUtf8.Get(), *SourceLength);
    return TS_STATUS_OK;
}

ts_status FAriadneNativeRuntime::HandleHostInvoke(
    void* UserData,
    const char* Method,
    size_t MethodLength,
    const char* PayloadJson,
    size_t PayloadJsonLength,
    char* ResultBuffer,
    size_t ResultCapacity,
    size_t* ResultLength)
{
    auto* Self = static_cast<FAriadneNativeRuntime*>(UserData);
    if (!Self || !Self->HostInvoker.IsBound() || !ResultLength)
    {
        return TS_STATUS_HOST_ERROR;
    }

    const FString MethodText = Utf8ToString(Method, MethodLength);
    const FString PayloadText = Utf8ToString(PayloadJson, PayloadJsonLength);
    const FString Result = Self->HostInvoker.Execute(MethodText, PayloadText);
    FTCHARToUTF8 ResultUtf8(*Result);
    *ResultLength = static_cast<size_t>(ResultUtf8.Length());
    if (!ResultBuffer || ResultCapacity < *ResultLength)
    {
        return TS_STATUS_BUFFER_TOO_SMALL;
    }

    FMemory::Memcpy(ResultBuffer, ResultUtf8.Get(), *ResultLength);
    return TS_STATUS_OK;
}

FString FAriadneNativeRuntime::LastError() const
{
    if (!Runtime)
    {
        return TEXT("Runtime is not created.");
    }

    size_t ErrorLength = 0;
    const char* Error = ts_runtime_last_error(Runtime, &ErrorLength);
    return Error ? Utf8ToString(Error, ErrorLength) : TEXT("Unknown AriadneTS runtime error.");
}
