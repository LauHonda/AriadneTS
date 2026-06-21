#pragma once

#include "AriadneNativeRuntime.h"
#include "AriadneScriptPackageReader.h"
#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "AriadneTSRuntimeHost.generated.h"

UENUM(BlueprintType)
enum class EAriadneTSDebugProtocol : uint8
{
    None = 0,
    ChromeDevTools = 1,
    DebugAdapterProtocol = 2,
};

UCLASS(BlueprintType)
class ARIADNETS_API AAriadneTSRuntimeHost : public AActor
{
    GENERATED_BODY()

public:
    AAriadneTSRuntimeHost();

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS")
    FString PackagePath;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS", meta = (MultiLine = true))
    FString PackageSigningPublicKey;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS")
    bool bStartOnBeginPlay = true;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS")
    uint64 MemoryLimitBytes = 64ull * 1024ull * 1024ull;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS")
    uint64 MaxStackSizeBytes = 1024ull * 1024ull;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS")
    int32 ExecutionTimeoutMilliseconds = 1000;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS")
    int32 MaxJobsPerFrame = 1024;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS|Debug")
    bool bEnableScriptDebugging = false;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS|Debug")
    EAriadneTSDebugProtocol DebugProtocol = EAriadneTSDebugProtocol::ChromeDevTools;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS|Debug")
    FString DebugHost = TEXT("127.0.0.1");

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS|Debug")
    int32 DebugBasePort = 9229;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS|Debug")
    int32 DebugInstanceId = 0;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS|Debug")
    FString DebugRole = TEXT("Client");

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS|Debug")
    bool bWaitForDebugger = false;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AriadneTS|Debug", meta = (ClampMin = "0", ClampMax = "5000"))
    int32 DebugStartupGraceMilliseconds = 1000;

    UFUNCTION(BlueprintCallable, Category = "AriadneTS|Debug")
    int32 GetDebugPort() const;

    UFUNCTION(BlueprintCallable, Category = "AriadneTS")
    bool StartPackage(FString& OutError);

    UFUNCTION(BlueprintCallable, Category = "AriadneTS")
    void StopPackage();

    UFUNCTION(BlueprintCallable, Category = "AriadneTS")
    bool InvokeScript(const FString& Method, const FString& PayloadJson, FString& OutResult, FString& OutError);

protected:
    virtual void BeginPlay() override;
    virtual void Tick(float DeltaSeconds) override;
    virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

private:
    using FHostHandler = TFunction<FString(const FString&)>;

    FAriadneNativeRuntime Runtime;
    FAriadneScriptPackage ActivePackage;
    TMap<FString, FHostHandler> HostHandlers;
    TMap<int32, TWeakObjectPtr<AActor>> ScriptActors;
    TMap<int32, int32> ComponentActors;

    void RegisterBuiltInHostHandlers();
    void ApplyDebugCommandLineOverrides();
    void ReportDebugConfiguration() const;
    FString LoadModule(const FString& ModuleName) const;
    FString InvokeHost(const FString& Method, const FString& PayloadJson);
    void LogScriptMessage(const FString& Message) const;

    FString HandleScriptLog(const FString& PayloadJson) const;
    FString HandleNotImplemented(const FString& Method, const FString& PayloadJson) const;
    FString CreateScriptActor(const FString& PayloadJson);
    FString DestroyScriptActor(const FString& PayloadJson);
    FString SetActorTransform(const FString& PayloadJson);
    FString SetActorParent(const FString& PayloadJson);
    FString AddScriptComponent(const FString& PayloadJson);
    FString RemoveScriptComponent(const FString& PayloadJson);
    FString SetScriptComponentProperty(const FString& PayloadJson);

    bool TryGetJsonObject(const FString& PayloadJson, TSharedPtr<FJsonObject>& OutObject) const;
    bool TryGetScriptActor(int32 ActorId, AActor*& OutActor) const;
    static FVector JsonVector(const TSharedPtr<FJsonObject>& Object, const TCHAR* FieldName, const FVector& DefaultValue = FVector::ZeroVector);
    static FQuat JsonQuat(const TSharedPtr<FJsonObject>& Object, const TCHAR* FieldName, const FQuat& DefaultValue = FQuat::Identity);
    static FString Ok(const FString& ResultJson = TEXT("null"));
    static FString Error(const FString& Code, const FString& Message);
};
