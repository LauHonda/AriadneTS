#pragma once

#include "AriadneTSRuntimeHost.h"
#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"
#include "AriadneTSEditorSettings.generated.h"

UCLASS(Config = Game, DefaultConfig, DisplayName = "AriadneTS")
class UAriadneTSEditorSettings : public UDeveloperSettings
{
    GENERATED_BODY()

public:
    UPROPERTY(Config, EditAnywhere, Category = "Environment")
    FString NodeExecutable = TEXT("node");

    UPROPERTY(Config, EditAnywhere, Category = "TypeScript")
    FString TypeScriptRoot = TEXT("{ProjectDir}/TypeScript");

    UPROPERTY(Config, EditAnywhere, Category = "Build")
    FString PackageVersion = TEXT("0.2.0");

    UPROPERTY(Config, EditAnywhere, Category = "Build", meta = (ClampMin = "0"))
    int32 BuildNumber = 1;

    UPROPERTY(Config, EditAnywhere, Category = "Build")
    FString PrivateKeyPath = TEXT("{ProjectDir}/AriadneTS/dev-private-key.pem");

    UPROPERTY(Config, EditAnywhere, Category = "Build")
    FString OutputPackagePath = TEXT("{ContentDir}/AriadneTS/typescript-package.bytes");

    UPROPERTY(Config, EditAnywhere, Category = "Build", meta = (MultiLine = true))
    FString PackageSigningPublicKey;

    UPROPERTY(Config, EditAnywhere, Category = "Script Debugging")
    bool bEnableScriptDebugging = false;

    UPROPERTY(Config, EditAnywhere, Category = "Script Debugging")
    EAriadneTSDebugProtocol DebugProtocol = EAriadneTSDebugProtocol::ChromeDevTools;

    UPROPERTY(Config, EditAnywhere, Category = "Script Debugging")
    FString DebugHost = TEXT("127.0.0.1");

    UPROPERTY(Config, EditAnywhere, Category = "Script Debugging", meta = (ClampMin = "1", ClampMax = "65535"))
    int32 DebugBasePort = 9229;

    UPROPERTY(Config, EditAnywhere, Category = "Script Debugging", meta = (ClampMin = "0"))
    int32 DebugInstanceId = 0;

    UPROPERTY(Config, EditAnywhere, Category = "Script Debugging")
    FString DebugRole = TEXT("Client");

    UPROPERTY(Config, EditAnywhere, Category = "Script Debugging")
    bool bWaitForDebugger = false;

    virtual FName GetCategoryName() const override { return TEXT("Plugins"); }
};
