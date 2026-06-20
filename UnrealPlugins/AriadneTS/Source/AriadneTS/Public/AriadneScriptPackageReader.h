#pragma once

#include "AriadneTSTypes.h"
#include "CoreMinimal.h"

class ARIADNETS_API FAriadneScriptPackageReader
{
public:
    static bool ReadPackageFromFile(
        const FString& PackagePath,
        const FString& SigningPublicKey,
        FAriadneScriptPackage& OutPackage,
        FString& OutError);

private:
    static bool ReadPackage(
        const TArray<uint8>& Bytes,
        const FString& SigningPublicKey,
        FAriadneScriptPackage& OutPackage,
        FString& OutError);

    static FString NormalizePath(const FString& Path);
    static FString Sha256Hex(const TArray<uint8>& Bytes);
};
