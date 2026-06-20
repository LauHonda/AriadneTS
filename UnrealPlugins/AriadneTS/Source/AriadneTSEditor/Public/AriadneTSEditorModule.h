#pragma once

#include "Modules/ModuleManager.h"

class FAriadneTSEditorModule final : public IModuleInterface
{
public:
    virtual void StartupModule() override;
    virtual void ShutdownModule() override;

private:
    void RegisterMenus();
    void InitializeTypeScriptWorkspace();
    void GenerateDevelopmentPrivateKey();
    void BuildTypeScriptPackage();
    void InstallVsCodeDebugger();
    void CreateVsCodeDebugConfig();
    void CreateRuntimeHost();
    void RefreshPublicKey();
    FString PluginBaseDir() const;
    FString ResolveConfiguredPath(const FString& Value) const;
    bool RunNodeScript(const FString& ScriptPath, const FString& Arguments, FString& OutStdOut, FString& OutStdErr, int32& OutReturnCode) const;
    void Notify(const FText& Message, float ExpireDuration = 5.0f) const;
};
