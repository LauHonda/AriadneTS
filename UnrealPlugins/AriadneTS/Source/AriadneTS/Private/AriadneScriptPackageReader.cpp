#include "AriadneScriptPackageReader.h"

#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/SecureHash.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

namespace
{
constexpr uint32 PackageFormatVersion = 1;
constexpr uint32 RequiredRuntimeAbiVersion = 5;

bool ReadBytes(const TArray<uint8>& Bytes, int64& Offset, void* OutData, int64 Length)
{
    if (Length < 0 || Offset < 0 || Offset + Length > Bytes.Num())
    {
        return false;
    }
    FMemory::Memcpy(OutData, Bytes.GetData() + Offset, Length);
    Offset += Length;
    return true;
}

bool ReadUInt32(const TArray<uint8>& Bytes, int64& Offset, uint32& OutValue)
{
    return ReadBytes(Bytes, Offset, &OutValue, sizeof(uint32));
}

bool ReadUInt64(const TArray<uint8>& Bytes, int64& Offset, uint64& OutValue)
{
    return ReadBytes(Bytes, Offset, &OutValue, sizeof(uint64));
}

bool ReadArray(const TArray<uint8>& Bytes, int64& Offset, int64 Length, TArray<uint8>& OutValue)
{
    if (Length < 0 || Offset < 0 || Offset + Length > Bytes.Num())
    {
        return false;
    }
    OutValue.SetNumUninitialized(Length);
    if (Length > 0)
    {
        FMemory::Memcpy(OutValue.GetData(), Bytes.GetData() + Offset, Length);
    }
    Offset += Length;
    return true;
}

FString Utf8BytesToString(const TArray<uint8>& Bytes)
{
    if (Bytes.Num() == 0)
    {
        return FString();
    }
    FUTF8ToTCHAR Converter(reinterpret_cast<const ANSICHAR*>(Bytes.GetData()), Bytes.Num());
    return FString(Converter.Length(), Converter.Get());
}
}

bool FAriadneScriptPackageReader::ReadPackageFromFile(
    const FString& PackagePath,
    const FString& SigningPublicKey,
    FAriadneScriptPackage& OutPackage,
    FString& OutError)
{
    TArray<uint8> Bytes;
    if (!FFileHelper::LoadFileToArray(Bytes, *PackagePath))
    {
        OutError = FString::Printf(TEXT("Failed to read AriadneTS package: %s"), *PackagePath);
        return false;
    }

    return ReadPackage(Bytes, SigningPublicKey, OutPackage, OutError);
}

bool FAriadneScriptPackageReader::ReadPackage(
    const TArray<uint8>& Bytes,
    const FString& SigningPublicKey,
    FAriadneScriptPackage& OutPackage,
    FString& OutError)
{
    int64 Offset = 0;
    uint8 Magic[8] = {};
    if (!ReadBytes(Bytes, Offset, Magic, sizeof(Magic)) ||
        FMemory::Memcmp(Magic, "ARDPKG01", sizeof(Magic)) != 0)
    {
        OutError = TEXT("Invalid AriadneTS script package magic.");
        return false;
    }

    uint32 FormatVersion = 0;
    uint32 ManifestLength = 0;
    uint32 SignatureLength = 0;
    uint32 FileCount = 0;
    if (!ReadUInt32(Bytes, Offset, FormatVersion) ||
        !ReadUInt32(Bytes, Offset, ManifestLength) ||
        !ReadUInt32(Bytes, Offset, SignatureLength) ||
        !ReadUInt32(Bytes, Offset, FileCount))
    {
        OutError = TEXT("AriadneTS package header is truncated.");
        return false;
    }

    if (FormatVersion != PackageFormatVersion)
    {
        OutError = TEXT("Unsupported AriadneTS package format version.");
        return false;
    }

    TArray<uint8> ManifestBytes;
    TArray<uint8> SignatureBytes;
    if (!ReadArray(Bytes, Offset, ManifestLength, ManifestBytes) ||
        !ReadArray(Bytes, Offset, SignatureLength, SignatureBytes))
    {
        OutError = TEXT("AriadneTS package manifest or signature is truncated.");
        return false;
    }

    if (!SigningPublicKey.IsEmpty())
    {
        UE_LOG(LogTemp, Warning, TEXT("AriadneTS Unreal package signature verification is not implemented yet; validating manifest and file hashes only."));
    }

    const FString ManifestJson = Utf8BytesToString(ManifestBytes);
    TSharedPtr<FJsonObject> ManifestObject;
    const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(ManifestJson);
    if (!FJsonSerializer::Deserialize(Reader, ManifestObject) || !ManifestObject.IsValid())
    {
        OutError = TEXT("AriadneTS package manifest JSON is invalid.");
        return false;
    }

    FAriadneScriptPackageManifest Manifest;
    Manifest.Version = ManifestObject->GetStringField(TEXT("Version"));
    Manifest.BuildNumber = static_cast<int64>(ManifestObject->GetNumberField(TEXT("BuildNumber")));
    Manifest.RequiredRuntimeAbiVersion = static_cast<uint32>(ManifestObject->GetIntegerField(TEXT("RequiredRuntimeAbiVersion")));
    Manifest.EntryModule = NormalizePath(ManifestObject->GetStringField(TEXT("EntryModule")));
    if (Manifest.RequiredRuntimeAbiVersion != RequiredRuntimeAbiVersion)
    {
        OutError = FString::Printf(
            TEXT("AriadneTS package requires ABI %u, but Unreal plugin provides ABI %u."),
            Manifest.RequiredRuntimeAbiVersion,
            RequiredRuntimeAbiVersion);
        return false;
    }

    const TArray<TSharedPtr<FJsonValue>>* FilesJson = nullptr;
    if (!ManifestObject->TryGetArrayField(TEXT("Files"), FilesJson) || !FilesJson)
    {
        OutError = TEXT("AriadneTS package manifest has no files.");
        return false;
    }

    TMap<FString, FAriadneScriptPackageFile> ExpectedFiles;
    for (const TSharedPtr<FJsonValue>& FileValue : *FilesJson)
    {
        const TSharedPtr<FJsonObject> FileObject = FileValue->AsObject();
        if (!FileObject.IsValid())
        {
            OutError = TEXT("AriadneTS package file entry is invalid.");
            return false;
        }

        FAriadneScriptPackageFile File;
        File.Path = NormalizePath(FileObject->GetStringField(TEXT("Path")));
        File.SizeBytes = static_cast<int64>(FileObject->GetNumberField(TEXT("SizeBytes")));
        File.Sha256 = FileObject->GetStringField(TEXT("Sha256")).ToLower();
        ExpectedFiles.Add(File.Path, File);
        Manifest.Files.Add(File);
    }

    if (static_cast<uint32>(Manifest.Files.Num()) != FileCount)
    {
        OutError = TEXT("AriadneTS package file count does not match manifest.");
        return false;
    }

    FAriadneScriptPackage Package;
    Package.Manifest = Manifest;
    for (uint32 Index = 0; Index < FileCount; ++Index)
    {
        uint32 PathLength = 0;
        uint64 DataLength = 0;
        if (!ReadUInt32(Bytes, Offset, PathLength) || !ReadUInt64(Bytes, Offset, DataLength))
        {
            OutError = TEXT("AriadneTS package file header is truncated.");
            return false;
        }

        TArray<uint8> PathBytes;
        TArray<uint8> DataBytes;
        if (!ReadArray(Bytes, Offset, PathLength, PathBytes) ||
            !ReadArray(Bytes, Offset, static_cast<int64>(DataLength), DataBytes))
        {
            OutError = TEXT("AriadneTS package file data is truncated.");
            return false;
        }

        const FString Path = NormalizePath(Utf8BytesToString(PathBytes));
        const FAriadneScriptPackageFile* Expected = ExpectedFiles.Find(Path);
        if (!Expected)
        {
            OutError = FString::Printf(TEXT("Unexpected AriadneTS package file: %s"), *Path);
            return false;
        }
        if (Expected->SizeBytes != static_cast<int64>(DataLength))
        {
            OutError = FString::Printf(TEXT("AriadneTS package file size mismatch: %s"), *Path);
            return false;
        }
        if (!Sha256Hex(DataBytes).Equals(Expected->Sha256, ESearchCase::IgnoreCase))
        {
            OutError = FString::Printf(TEXT("AriadneTS package file hash mismatch: %s"), *Path);
            return false;
        }

        Package.Modules.Add(Path, Utf8BytesToString(DataBytes));
    }

    OutPackage = MoveTemp(Package);
    return true;
}

FString FAriadneScriptPackageReader::NormalizePath(const FString& Path)
{
    FString Normalized = Path;
    Normalized.ReplaceInline(TEXT("\\"), TEXT("/"));
    if (Normalized.StartsWith(TEXT("./")))
    {
        Normalized.RightChopInline(2);
    }
    return Normalized;
}

FString FAriadneScriptPackageReader::Sha256Hex(const TArray<uint8>& Bytes)
{
    FSHA256Signature Hash;
    FSHA256::HashBuffer(Bytes.GetData(), Bytes.Num(), Hash.Hash);
    return BytesToHex(Hash.Hash, UE_ARRAY_COUNT(Hash.Hash)).ToLower();
}
