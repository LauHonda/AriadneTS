#pragma once

#include "Modules/ModuleManager.h"

class FAriadneTSEditorModule final : public IModuleInterface
{
public:
    virtual void StartupModule() override;
    virtual void ShutdownModule() override;

private:
    void RegisterMenus();
    void InstallProjectNodeToolchain();
    void DiagnoseTypeScriptEnvironment();
    void InitializeTypeScriptWorkspace();
    void InstallTypeScriptDependencies();
    void GenerateDevelopmentPrivateKey();
    void BuildTypeScriptPackage();
    void InstallVsCodeDebugger();
    void CreateVsCodeDebugConfig();
    void CreateRuntimeHost();
    void RefreshPublicKey();
    FString PluginBaseDir() const;
    FString ResolveConfiguredPath(const FString& Value) const;
    FString ResolveNpmExecutable() const;
    FString ResolveNpmCliScript() const;
    FString ProjectNodeToolchainRoot() const;
    FString CreateNodeDownloadUrl(FString& OutArchiveFileName, bool& bOutIsZip) const;
    bool ExtractNodeArchive(const FString& ArchivePath, const FString& ExtractRoot, bool bIsZip, FString& OutError) const;
    bool IsNpmCompatible(const FString& NpmCommand, const FString& NpmArguments, FString& OutReason) const;
    int32 ReadNodeMajorVersion() const;
    static int32 ParseMajorVersion(const FString& VersionText);
    bool RunNodeScript(const FString& ScriptPath, const FString& Arguments, FString& OutStdOut, FString& OutStdErr, int32& OutReturnCode) const;
    void Notify(const FText& Message, float ExpireDuration = 5.0f) const;
};
