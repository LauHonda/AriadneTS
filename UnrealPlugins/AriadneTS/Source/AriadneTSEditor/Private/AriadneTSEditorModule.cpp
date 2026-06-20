#include "AriadneTSEditorModule.h"

#include "AriadneTSEditorSettings.h"
#include "AriadneTSRuntimeHost.h"
#include "Editor.h"
#include "Engine/Selection.h"
#include "Engine/World.h"
#include "Framework/Notifications/NotificationManager.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Modules/ModuleManager.h"
#include "ToolMenus.h"
#include "Widgets/Notifications/SNotificationList.h"

#define LOCTEXT_NAMESPACE "FAriadneTSEditorModule"

void FAriadneTSEditorModule::StartupModule()
{
    UToolMenus::RegisterStartupCallback(
        FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &FAriadneTSEditorModule::RegisterMenus));
}

void FAriadneTSEditorModule::ShutdownModule()
{
    if (UToolMenus::IsToolMenusAvailable())
    {
        UToolMenus::UnregisterOwner(this);
    }
}

void FAriadneTSEditorModule::RegisterMenus()
{
    UToolMenu* Menu = UToolMenus::Get()->ExtendMenu(TEXT("LevelEditor.MainMenu.Tools"));
    FToolMenuSection& Section = Menu->FindOrAddSection(TEXT("AriadneTS"));
    Section.AddMenuEntry(
        TEXT("AriadneTSInitialize"),
        LOCTEXT("AriadneTSInitialize", "AriadneTS: Initialize TypeScript Workspace"),
        LOCTEXT("AriadneTSInitializeTooltip", "Create a TypeScript workspace in the Unreal project root."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::InitializeTypeScriptWorkspace)));
    Section.AddMenuEntry(
        TEXT("AriadneTSBuild"),
        LOCTEXT("AriadneTSBuild", "AriadneTS: Build TypeScript Package"),
        LOCTEXT("AriadneTSBuildTooltip", "Compile TypeScript and package it for the AriadneTS runtime."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::BuildTypeScriptPackage)));
    Section.AddMenuEntry(
        TEXT("AriadneTSGenerateKey"),
        LOCTEXT("AriadneTSGenerateKey", "AriadneTS: Generate Development Private Key"),
        LOCTEXT("AriadneTSGenerateKeyTooltip", "Generate the configured development RSA private key and refresh the public key."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::GenerateDevelopmentPrivateKey)));
    Section.AddMenuEntry(
        TEXT("AriadneTSInstallVsCode"),
        LOCTEXT("AriadneTSInstallVsCode", "AriadneTS: Install VSCode Debugger And Config"),
        LOCTEXT("AriadneTSInstallVsCodeTooltip", "Install the AriadneTS VSCode debug adapter and create .vscode/launch.json."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::InstallVsCodeDebugger)));
    Section.AddMenuEntry(
        TEXT("AriadneTSCreateVsCodeConfig"),
        LOCTEXT("AriadneTSCreateVsCodeConfig", "AriadneTS: Create VSCode Debug Config"),
        LOCTEXT("AriadneTSCreateVsCodeConfigTooltip", "Create or update the AriadneTS attach configuration in .vscode/launch.json."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::CreateVsCodeDebugConfig)));
    Section.AddMenuEntry(
        TEXT("AriadneTSCreateRuntimeHost"),
        LOCTEXT("AriadneTSCreateRuntimeHost", "AriadneTS: Create Runtime Host"),
        LOCTEXT("AriadneTSCreateRuntimeHostTooltip", "Add an AriadneTS runtime host actor to the current level using Project Settings defaults."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::CreateRuntimeHost)));
}

void FAriadneTSEditorModule::InitializeTypeScriptWorkspace()
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString TargetRoot = ResolveConfiguredPath(Settings->TypeScriptRoot);
    const bool bInitialized = FPaths::FileExists(FPaths::Combine(TargetRoot, TEXT("package.json"))) &&
        FPaths::FileExists(FPaths::Combine(TargetRoot, TEXT("tsconfig.json"))) &&
        FPaths::DirectoryExists(FPaths::Combine(TargetRoot, TEXT("src")));
    if (bInitialized)
    {
        Notify(LOCTEXT("AriadneTSTypeScriptExists", "AriadneTS TypeScript workspace already exists."));
        return;
    }

    const FString TemplateRoot = FPaths::Combine(PluginBaseDir(), TEXT("Resources/TypeScriptTemplate"));
    IFileManager::Get().CopyDirectoryTree(*TargetRoot, *TemplateRoot, true);

    CreateVsCodeDebugConfig();
    Notify(LOCTEXT("AriadneTSTypeScriptCreated", "AriadneTS TypeScript workspace initialized."));
}

void FAriadneTSEditorModule::GenerateDevelopmentPrivateKey()
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString ScriptPath = FPaths::ConvertRelativePathToFull(
        FPaths::Combine(PluginBaseDir(), TEXT("Scripts/build_script_package.mjs")));
    const FString PrivateKeyPath = ResolveConfiguredPath(Settings->PrivateKeyPath);
    IFileManager::Get().MakeDirectory(*FPaths::GetPath(PrivateKeyPath), true);

    int32 ReturnCode = 0;
    FString StdOut;
    FString StdErr;
    const FString GenerateArgs = FString::Printf(
        TEXT("--generate-private-key --private-key \"%s\""),
        *PrivateKeyPath);
    if (!RunNodeScript(ScriptPath, GenerateArgs, StdOut, StdErr, ReturnCode) || ReturnCode != 0)
    {
        Notify(LOCTEXT("AriadneTSKeyFailed", "Failed to generate AriadneTS development private key."), 8.0f);
        UE_LOG(LogTemp, Error, TEXT("AriadneTS key generation failed: %s\n%s"), *StdOut, *StdErr);
        return;
    }

    RefreshPublicKey();
    Notify(LOCTEXT("AriadneTSKeySucceeded", "AriadneTS development private key generated."));
}

void FAriadneTSEditorModule::BuildTypeScriptPackage()
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString ScriptPath = FPaths::ConvertRelativePathToFull(
        FPaths::Combine(PluginBaseDir(), TEXT("Scripts/build_script_package.mjs")));
    const FString TypeScriptRoot = ResolveConfiguredPath(Settings->TypeScriptRoot);
    const FString OutputPath = ResolveConfiguredPath(Settings->OutputPackagePath);
    const FString PrivateKeyPath = ResolveConfiguredPath(Settings->PrivateKeyPath);
    IFileManager::Get().MakeDirectory(*FPaths::GetPath(OutputPath), true);
    if (!FPaths::FileExists(PrivateKeyPath))
    {
        GenerateDevelopmentPrivateKey();
    }

    int32 ReturnCode = 0;
    FString StdOut;
    FString StdErr;
    const FString Args = FString::Printf(
        TEXT("--ts-root \"%s\" --version \"%s\" --build-number %d --private-key \"%s\" --output \"%s\" --required-abi 4"),
        *TypeScriptRoot,
        *Settings->PackageVersion,
        FMath::Max(0, Settings->BuildNumber),
        *PrivateKeyPath,
        *OutputPath);

    RunNodeScript(ScriptPath, Args, StdOut, StdErr, ReturnCode);
    const bool bSucceeded = ReturnCode == 0;
    if (!bSucceeded)
    {
        UE_LOG(LogTemp, Error, TEXT("AriadneTS package build failed: %s\n%s"), *StdOut, *StdErr);
    }

    if (bSucceeded)
    {
        RefreshPublicKey();
    }
    Notify(bSucceeded
        ? LOCTEXT("AriadneTSBuildSucceeded", "AriadneTS package build completed.")
        : LOCTEXT("AriadneTSBuildFailed", "AriadneTS package build failed. Check the Editor log."),
        6.0f);
}

void FAriadneTSEditorModule::InstallVsCodeDebugger()
{
    const FString ScriptPath = FPaths::ConvertRelativePathToFull(
        FPaths::Combine(PluginBaseDir(), TEXT("Scripts/install_vscode_debugger_extension.mjs")));
    int32 ReturnCode = 0;
    FString StdOut;
    FString StdErr;
    RunNodeScript(ScriptPath, TEXT(""), StdOut, StdErr, ReturnCode);
    if (ReturnCode != 0)
    {
        Notify(LOCTEXT("AriadneTSVsCodeInstallFailed", "Failed to install AriadneTS VSCode debugger. Check the Editor log."), 8.0f);
        UE_LOG(LogTemp, Error, TEXT("AriadneTS VSCode debugger install failed: %s\n%s"), *StdOut, *StdErr);
        return;
    }

    CreateVsCodeDebugConfig();
    Notify(LOCTEXT("AriadneTSVsCodeInstallSucceeded", "AriadneTS VSCode debugger installed. Restart VSCode or reload the window."), 8.0f);
}

void FAriadneTSEditorModule::CreateVsCodeDebugConfig()
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString VscodeDirectory = FPaths::Combine(FPaths::ProjectDir(), TEXT(".vscode"));
    const FString LaunchJsonPath = FPaths::Combine(VscodeDirectory, TEXT("launch.json"));
    IFileManager::Get().MakeDirectory(*VscodeDirectory, true);

    const int32 Port = FMath::Clamp(Settings->DebugBasePort + Settings->DebugInstanceId, 1, 65535);
    const FString TypeScriptRoot = ResolveConfiguredPath(Settings->TypeScriptRoot);
    FString RelativeTypeScriptRoot = TypeScriptRoot;
    FPaths::MakePathRelativeTo(RelativeTypeScriptRoot, *FPaths::ProjectDir());
    RelativeTypeScriptRoot = RelativeTypeScriptRoot.Replace(TEXT("\\"), TEXT("/")).TrimStartAndEnd();
    if (RelativeTypeScriptRoot.IsEmpty() || RelativeTypeScriptRoot.StartsWith(TEXT("..")))
    {
        RelativeTypeScriptRoot = TEXT("TypeScript");
    }

    const FString LaunchJson = FString::Printf(
        TEXT("{\n")
        TEXT("  \"version\": \"0.2.0\",\n")
        TEXT("  \"configurations\": [\n")
        TEXT("    {\n")
        TEXT("      \"type\": \"ariadnets\",\n")
        TEXT("      \"request\": \"attach\",\n")
        TEXT("      \"name\": \"Attach AriadneTS\",\n")
        TEXT("      \"host\": \"%s\",\n")
        TEXT("      \"port\": %d,\n")
        TEXT("      \"tsRoot\": \"${workspaceFolder}/%s\",\n")
        TEXT("      \"pollIntervalMs\": 250\n")
        TEXT("    }\n")
        TEXT("  ]\n")
        TEXT("}\n"),
        *Settings->DebugHost.Replace(TEXT("\\"), TEXT("\\\\")).Replace(TEXT("\""), TEXT("\\\"")),
        Port,
        *RelativeTypeScriptRoot.Replace(TEXT("\\"), TEXT("/")).Replace(TEXT("\""), TEXT("\\\"")));

    if (FFileHelper::SaveStringToFile(LaunchJson, *LaunchJsonPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
    {
        Notify(LOCTEXT("AriadneTSVsCodeConfigCreated", "AriadneTS VSCode launch.json created or updated."));
    }
    else
    {
        Notify(LOCTEXT("AriadneTSVsCodeConfigFailed", "Failed to write AriadneTS VSCode launch.json."), 8.0f);
    }
}

void FAriadneTSEditorModule::CreateRuntimeHost()
{
    UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
    if (!World)
    {
        FNotificationInfo Info(LOCTEXT("AriadneTSNoWorld", "No editor world is available."));
        Info.ExpireDuration = 4.0f;
        FSlateNotificationManager::Get().AddNotification(Info);
        return;
    }

    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    FActorSpawnParameters SpawnParameters;
    SpawnParameters.Name = MakeUniqueObjectName(World, AAriadneTSRuntimeHost::StaticClass(), TEXT("AriadneTSRuntimeHost"));
    AAriadneTSRuntimeHost* Host = World->SpawnActor<AAriadneTSRuntimeHost>(
        AAriadneTSRuntimeHost::StaticClass(),
        FTransform::Identity,
        SpawnParameters);
    if (!Host)
    {
        FNotificationInfo Info(LOCTEXT("AriadneTSCreateHostFailed", "Failed to create AriadneTS runtime host."));
        Info.ExpireDuration = 4.0f;
        FSlateNotificationManager::Get().AddNotification(Info);
        return;
    }

    Host->PackagePath = ResolveConfiguredPath(Settings->OutputPackagePath);
    Host->PackageSigningPublicKey = Settings->PackageSigningPublicKey;
    Host->bEnableScriptDebugging = Settings->bEnableScriptDebugging;
    Host->DebugProtocol = Settings->DebugProtocol;
    Host->DebugHost = Settings->DebugHost;
    Host->DebugBasePort = Settings->DebugBasePort;
    Host->DebugInstanceId = Settings->DebugInstanceId;
    Host->DebugRole = Settings->DebugRole;
    Host->bWaitForDebugger = Settings->bWaitForDebugger;

    GEditor->SelectNone(false, true);
    GEditor->SelectActor(Host, true, true);

    FNotificationInfo Info(FText::Format(
        LOCTEXT("AriadneTSCreateHostSucceeded", "AriadneTS runtime host created. Debug port: {0}"),
        FText::AsNumber(Host->GetDebugPort())));
    Info.ExpireDuration = 5.0f;
    FSlateNotificationManager::Get().AddNotification(Info);
}

FString FAriadneTSEditorModule::PluginBaseDir() const
{
    const TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(TEXT("AriadneTS"));
    return Plugin.IsValid() ? Plugin->GetBaseDir() : FString();
}

void FAriadneTSEditorModule::RefreshPublicKey()
{
    UAriadneTSEditorSettings* Settings = GetMutableDefault<UAriadneTSEditorSettings>();
    const FString PrivateKeyPath = ResolveConfiguredPath(Settings->PrivateKeyPath);
    if (!FPaths::FileExists(PrivateKeyPath))
    {
        return;
    }

    const FString ScriptPath = FPaths::ConvertRelativePathToFull(
        FPaths::Combine(PluginBaseDir(), TEXT("Scripts/build_script_package.mjs")));
    int32 ReturnCode = 0;
    FString StdOut;
    FString StdErr;
    const FString Args = FString::Printf(TEXT("--print-public-key --private-key \"%s\""), *PrivateKeyPath);
    RunNodeScript(ScriptPath, Args, StdOut, StdErr, ReturnCode);
    if (ReturnCode != 0)
    {
        UE_LOG(LogTemp, Warning, TEXT("AriadneTS public key refresh failed: %s\n%s"), *StdOut, *StdErr);
        return;
    }

    Settings->PackageSigningPublicKey = StdOut.TrimStartAndEnd();
    Settings->SaveConfig();
}

FString FAriadneTSEditorModule::ResolveConfiguredPath(const FString& Value) const
{
    FString Resolved = Value;
    Resolved.ReplaceInline(TEXT("{ProjectDir}"), *FPaths::ProjectDir());
    Resolved.ReplaceInline(TEXT("{ContentDir}"), *FPaths::ProjectContentDir());
    Resolved.ReplaceInline(TEXT("{PluginDir}"), *PluginBaseDir());
    return FPaths::ConvertRelativePathToFull(Resolved);
}

bool FAriadneTSEditorModule::RunNodeScript(
    const FString& ScriptPath,
    const FString& Arguments,
    FString& OutStdOut,
    FString& OutStdErr,
    int32& OutReturnCode) const
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString NodeExecutable = Settings->NodeExecutable.IsEmpty() ? FString(TEXT("node")) : Settings->NodeExecutable;
    const FString CommandLine = FString::Printf(TEXT("\"%s\" %s"), *ScriptPath, *Arguments);
    return FPlatformProcess::ExecProcess(*NodeExecutable, *CommandLine, &OutReturnCode, &OutStdOut, &OutStdErr);
}

void FAriadneTSEditorModule::Notify(const FText& Message, float ExpireDuration) const
{
    FNotificationInfo Info(Message);
    Info.ExpireDuration = ExpireDuration;
    FSlateNotificationManager::Get().AddNotification(Info);
}

#undef LOCTEXT_NAMESPACE

IMPLEMENT_MODULE(FAriadneTSEditorModule, AriadneTSEditor)
