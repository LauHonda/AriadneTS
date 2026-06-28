#pragma once

#include "AriadneTSTypes.h"
#include "CoreMinimal.h"

THIRD_PARTY_INCLUDES_START
#include "ariadnets/ts_runtime.h"
THIRD_PARTY_INCLUDES_END

class ARIADNETS_API FAriadneNativeRuntime
{
public:
    static constexpr uint32 RequiredAbiVersion = 5;

    FAriadneNativeRuntime();
    ~FAriadneNativeRuntime();

    bool Create(
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
        FString& OutError);

    void Destroy();

    bool EvaluateModule(const FString& Source, const FString& ModuleName, FString& OutError);
    bool Invoke(const FString& Method, const FString& PayloadJson, FString& OutResult, FString& OutError);
    bool ExecutePendingJobs(uint32 MaxJobs, uint32& OutExecutedJobs, FString& OutError);
    bool IsCreated() const { return Runtime != nullptr; }

private:
    ts_runtime* Runtime = nullptr;
    FAriadneLogDelegate Log;
    FAriadneModuleLoadDelegate ModuleLoader;
    FAriadneHostInvokeDelegate HostInvoker;
    TArray<FTCHARToUTF8> PinnedUtf8;
    TArray<ANSICHAR> PendingHostResult;

    static void HandleLog(void* UserData, const char* Message, size_t MessageLength);
    static ts_status HandleModuleLoad(
        void* UserData,
        const char* ModuleName,
        size_t ModuleNameLength,
        char* SourceBuffer,
        size_t SourceCapacity,
        size_t* SourceLength);
    static ts_status HandleHostInvoke(
        void* UserData,
        const char* Method,
        size_t MethodLength,
        const char* PayloadJson,
        size_t PayloadJsonLength,
        char* ResultBuffer,
        size_t ResultCapacity,
        size_t* ResultLength);

    FString LastError() const;
};
