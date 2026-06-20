#pragma once

#include "CoreMinimal.h"

struct FAriadneScriptPackageFile
{
    FString Path;
    int64 SizeBytes = 0;
    FString Sha256;
};

struct FAriadneScriptPackageManifest
{
    FString Version;
    int64 BuildNumber = 0;
    uint32 RequiredRuntimeAbiVersion = 0;
    FString EntryModule;
    TArray<FAriadneScriptPackageFile> Files;
};

struct FAriadneScriptPackage
{
    FAriadneScriptPackageManifest Manifest;
    TMap<FString, FString> Modules;

    FString LoadModule(const FString& ModuleName) const;
};

DECLARE_DELEGATE_OneParam(FAriadneLogDelegate, const FString&);
DECLARE_DELEGATE_RetVal_OneParam(FString, FAriadneModuleLoadDelegate, const FString&);
DECLARE_DELEGATE_RetVal_TwoParams(FString, FAriadneHostInvokeDelegate, const FString&, const FString&);
