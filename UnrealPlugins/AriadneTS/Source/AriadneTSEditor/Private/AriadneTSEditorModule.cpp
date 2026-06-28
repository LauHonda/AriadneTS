#include "AriadneTSEditorModule.h"

#include "AriadneTSEditorSettings.h"
#include "AriadneTSRuntimeHost.h"
#include "Editor.h"
#include "Engine/Selection.h"
#include "Engine/World.h"
#include "Framework/Notifications/NotificationManager.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Guid.h"
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
    FToolMenuSection& EnvironmentSection = Menu->FindOrAddSection(TEXT("AriadneTSEnvironmentSetup"));
    EnvironmentSection.Label = LOCTEXT("AriadneTSEnvironmentSetup", "AriadneTS Environment Setup");
    EnvironmentSection.AddMenuEntry(
        TEXT("AriadneTSInstallProjectNodeToolchain"),
        LOCTEXT("AriadneTSInstallProjectNodeToolchain", "Install/Change Project Node.js Toolchain"),
        LOCTEXT("AriadneTSInstallProjectNodeToolchainTooltip", "Download Node.js into AriadneTS/Toolchain/node under the Unreal project root."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::InstallProjectNodeToolchain)));
    EnvironmentSection.AddMenuEntry(
        TEXT("AriadneTSDiagnoseTypeScriptEnvironment"),
        LOCTEXT("AriadneTSDiagnoseTypeScriptEnvironment", "Diagnose TypeScript Environment"),
        LOCTEXT("AriadneTSDiagnoseTypeScriptEnvironmentTooltip", "Check Node.js, npm, TypeScript workspace, and local TypeScript compiler state."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::DiagnoseTypeScriptEnvironment)));
    EnvironmentSection.AddMenuEntry(
        TEXT("AriadneTSInitialize"),
        LOCTEXT("AriadneTSInitialize", "Initialize TypeScript Workspace"),
        LOCTEXT("AriadneTSInitializeTooltip", "Create a TypeScript workspace in the Unreal project root."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::InitializeTypeScriptWorkspace)));
    EnvironmentSection.AddMenuEntry(
        TEXT("AriadneTSInstallTypeScriptDependencies"),
        LOCTEXT("AriadneTSInstallTypeScriptDependencies", "Install Local TypeScript Compiler"),
        LOCTEXT("AriadneTSInstallTypeScriptDependenciesTooltip", "Install the local TypeScript compiler used to build AriadneTS script packages."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::InstallTypeScriptDependencies)));
    EnvironmentSection.AddMenuEntry(
        TEXT("AriadneTSInstallVsCode"),
        LOCTEXT("AriadneTSInstallVsCode", "Install VSCode Debugger And Config"),
        LOCTEXT("AriadneTSInstallVsCodeTooltip", "Install the AriadneTS VSCode debug adapter and create .vscode/launch.json."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::InstallVsCodeDebugger)));

    FToolMenuSection& PackageSection = Menu->FindOrAddSection(TEXT("AriadneTSPackageSigningAndBuild"));
    PackageSection.Label = LOCTEXT("AriadneTSPackageSigningAndBuild", "AriadneTS Package Signing And Build");
    PackageSection.AddMenuEntry(
        TEXT("AriadneTSGenerateKey"),
        LOCTEXT("AriadneTSGenerateKey", "Generate Development Private Key"),
        LOCTEXT("AriadneTSGenerateKeyTooltip", "Generate the configured development RSA private key and refresh the public key."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::GenerateDevelopmentPrivateKey)));
    PackageSection.AddMenuEntry(
        TEXT("AriadneTSBuild"),
        LOCTEXT("AriadneTSBuild", "Build TypeScript Package"),
        LOCTEXT("AriadneTSBuildTooltip", "Compile TypeScript and package it for the AriadneTS runtime."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::BuildTypeScriptPackage)));

    FToolMenuSection& RuntimeSection = Menu->FindOrAddSection(TEXT("AriadneTSRuntimeAndDebugging"));
    RuntimeSection.Label = LOCTEXT("AriadneTSRuntimeAndDebugging", "AriadneTS Runtime And Debugging");
    RuntimeSection.AddMenuEntry(
        TEXT("AriadneTSCreateVsCodeConfig"),
        LOCTEXT("AriadneTSCreateVsCodeConfig", "Create VSCode Debug Config"),
        LOCTEXT("AriadneTSCreateVsCodeConfigTooltip", "Create or update the AriadneTS attach configuration in .vscode/launch.json."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::CreateVsCodeDebugConfig)));
    RuntimeSection.AddMenuEntry(
        TEXT("AriadneTSCreateRuntimeHost"),
        LOCTEXT("AriadneTSCreateRuntimeHost", "Create Runtime Host"),
        LOCTEXT("AriadneTSCreateRuntimeHostTooltip", "Add an AriadneTS runtime host actor to the current level using Project Settings defaults."),
        FSlateIcon(),
        FUIAction(FExecuteAction::CreateRaw(this, &FAriadneTSEditorModule::CreateRuntimeHost)));
}

void FAriadneTSEditorModule::InstallProjectNodeToolchain()
{
    FString ArchiveFileName;
    bool bIsZip = false;
    const FString Url = CreateNodeDownloadUrl(ArchiveFileName, bIsZip);
    const FString TempRoot = FPaths::Combine(FPaths::ProjectIntermediateDir(), TEXT("AriadneTSNode"), FGuid::NewGuid().ToString(EGuidFormats::Digits));
    const FString ArchivePath = FPaths::Combine(TempRoot, ArchiveFileName);
    const FString ExtractRoot = FPaths::Combine(TempRoot, TEXT("extract"));
    IFileManager::Get().MakeDirectory(*ExtractRoot, true);

    int32 ReturnCode = 0;
    FString StdOut;
    FString StdErr;
    const FString CurlArgs = FString::Printf(TEXT("-L \"%s\" -o \"%s\""), *Url, *ArchivePath);
    FPlatformProcess::ExecProcess(TEXT("curl"), *CurlArgs, &ReturnCode, &StdOut, &StdErr);
    if (ReturnCode != 0)
    {
        Notify(LOCTEXT("AriadneTSNodeDownloadFailed", "Failed to download AriadneTS project Node.js toolchain. Check the Editor log."), 8.0f);
        UE_LOG(LogTemp, Error, TEXT("AriadneTS Node.js download failed: %s\n%s"), *StdOut, *StdErr);
        IFileManager::Get().DeleteDirectory(*TempRoot, false, true);
        return;
    }

    FString ExtractError;
    if (!ExtractNodeArchive(ArchivePath, ExtractRoot, bIsZip, ExtractError))
    {
        Notify(LOCTEXT("AriadneTSNodeExtractFailed", "Failed to extract AriadneTS project Node.js toolchain. Check the Editor log."), 8.0f);
        UE_LOG(LogTemp, Error, TEXT("%s"), *ExtractError);
        IFileManager::Get().DeleteDirectory(*TempRoot, false, true);
        return;
    }

    TArray<FString> ExtractedDirectories;
    IFileManager::Get().FindFiles(ExtractedDirectories, *FPaths::Combine(ExtractRoot, TEXT("*")), false, true);
    if (ExtractedDirectories.Num() != 1)
    {
        Notify(LOCTEXT("AriadneTSNodeExtractShapeFailed", "Node.js archive layout was not recognized. Check the Editor log."), 8.0f);
        UE_LOG(LogTemp, Error, TEXT("Unexpected Node.js archive layout under: %s"), *ExtractRoot);
        IFileManager::Get().DeleteDirectory(*TempRoot, false, true);
        return;
    }

    const FString SourceDirectory = FPaths::Combine(ExtractRoot, ExtractedDirectories[0]);
    const FString TargetDirectory = ProjectNodeToolchainRoot();
    IFileManager::Get().DeleteDirectory(*TargetDirectory, false, true);
    IFileManager::Get().MakeDirectory(*FPaths::GetPath(TargetDirectory), true);
    if (!IFileManager::Get().Move(*TargetDirectory, *SourceDirectory, true, true))
    {
        Notify(LOCTEXT("AriadneTSNodeInstallFailed", "Failed to install AriadneTS project Node.js toolchain. Check the Editor log."), 8.0f);
        UE_LOG(LogTemp, Error, TEXT("Failed to move Node.js toolchain from %s to %s"), *SourceDirectory, *TargetDirectory);
        IFileManager::Get().DeleteDirectory(*TempRoot, false, true);
        return;
    }

    IFileManager::Get().DeleteDirectory(*TempRoot, false, true);
    Notify(LOCTEXT("AriadneTSNodeInstalled", "AriadneTS project Node.js toolchain installed."));
}

void FAriadneTSEditorModule::DiagnoseTypeScriptEnvironment()
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString TypeScriptRoot = ResolveConfiguredPath(Settings->TypeScriptRoot);
    const bool bTypeScriptProjectReady =
        FPaths::FileExists(FPaths::Combine(TypeScriptRoot, TEXT("package.json"))) &&
        FPaths::FileExists(FPaths::Combine(TypeScriptRoot, TEXT("tsconfig.json"))) &&
        FPaths::DirectoryExists(FPaths::Combine(TypeScriptRoot, TEXT("src")));
    const bool bLocalTypeScriptCompilerReady =
        FPaths::FileExists(FPaths::Combine(TypeScriptRoot, TEXT("node_modules/typescript/package.json"))) &&
        (FPaths::FileExists(FPaths::Combine(TypeScriptRoot, TEXT("node_modules/typescript/bin/tsc"))) ||
         FPaths::FileExists(FPaths::Combine(TypeScriptRoot, TEXT("node_modules/typescript/lib/tsc.js"))));

    const FString NpmCliScript = ResolveNpmCliScript();
    const FString NpmExecutable = NpmCliScript.IsEmpty() ? ResolveNpmExecutable() : NpmCliScript;
    FString CompatibilityReason;
    const bool bNpmReady = !NpmExecutable.IsEmpty() && IsNpmCompatible(NpmExecutable, TEXT("--version"), CompatibilityReason);

    UE_LOG(LogTemp, Display, TEXT("AriadneTS TypeScript Environment"));
    UE_LOG(LogTemp, Display, TEXT("TypeScript Root: %s"), *TypeScriptRoot);
    UE_LOG(LogTemp, Display, TEXT("Target Node Version: %s"), *Settings->NodeVersion);
    UE_LOG(LogTemp, Display, TEXT("TypeScript Project: %s"), bTypeScriptProjectReady ? TEXT("Ready") : TEXT("Missing or incomplete"));
    UE_LOG(LogTemp, Display, TEXT("Node Executable: %s"), *ResolveConfiguredPath(Settings->NodeExecutable));
    UE_LOG(LogTemp, Display, TEXT("Node Major Version: %d"), ReadNodeMajorVersion());
    UE_LOG(LogTemp, Display, TEXT("npm: %s"), NpmExecutable.IsEmpty() ? TEXT("Not found") : *NpmExecutable);
    UE_LOG(LogTemp, Display, TEXT("Node/npm Compatibility: %s"), bNpmReady ? TEXT("Ready") : TEXT("Incompatible"));
    if (!CompatibilityReason.IsEmpty())
    {
        UE_LOG(LogTemp, Warning, TEXT("%s"), *CompatibilityReason);
    }
    UE_LOG(LogTemp, Display, TEXT("Local TypeScript Compiler: %s"), bLocalTypeScriptCompilerReady ? TEXT("Ready") : TEXT("Missing"));

    Notify(bNpmReady && bTypeScriptProjectReady && bLocalTypeScriptCompilerReady
        ? LOCTEXT("AriadneTSEnvironmentReady", "AriadneTS TypeScript environment is ready.")
        : LOCTEXT("AriadneTSEnvironmentNeedsAttention", "AriadneTS TypeScript environment needs attention. Check the Editor log."),
        8.0f);
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

void FAriadneTSEditorModule::InstallTypeScriptDependencies()
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString TypeScriptRoot = ResolveConfiguredPath(Settings->TypeScriptRoot);
    if (!FPaths::FileExists(FPaths::Combine(TypeScriptRoot, TEXT("package.json"))))
    {
        Notify(LOCTEXT("AriadneTSTypeScriptPackageMissing", "Initialize the AriadneTS TypeScript workspace before installing the local TypeScript compiler."), 8.0f);
        return;
    }

    int32 ReturnCode = 0;
    FString StdOut;
    FString StdErr;
    const FString NpmCliScript = ResolveNpmCliScript();
    if (!NpmCliScript.IsEmpty() && FPaths::FileExists(ResolveConfiguredPath(Settings->NodeExecutable)))
    {
        FString CompatibilityReason;
        if (!IsNpmCompatible(NpmCliScript, TEXT("--version"), CompatibilityReason))
        {
            Notify(FText::FromString(CompatibilityReason), 10.0f);
            UE_LOG(LogTemp, Error, TEXT("%s"), *CompatibilityReason);
            return;
        }

        const FString Args = FString::Printf(TEXT("--prefix \"%s\" install"), *TypeScriptRoot);
        RunNodeScript(NpmCliScript, Args, StdOut, StdErr, ReturnCode);
    }
    else
    {
        const FString NpmExecutable = ResolveNpmExecutable();
        if (NpmExecutable.IsEmpty())
        {
            Notify(LOCTEXT("AriadneTSNpmMissing", "Could not find npm. Install Node.js with npm or configure the Node executable in Project Settings."), 8.0f);
            return;
        }

        FString CompatibilityReason;
        if (!IsNpmCompatible(NpmExecutable, TEXT("--version"), CompatibilityReason))
        {
            Notify(FText::FromString(CompatibilityReason), 10.0f);
            UE_LOG(LogTemp, Error, TEXT("%s"), *CompatibilityReason);
            return;
        }

        const FString Args = FString::Printf(TEXT("--prefix \"%s\" install"), *TypeScriptRoot);
        FPlatformProcess::ExecProcess(*NpmExecutable, *Args, &ReturnCode, &StdOut, &StdErr);
    }
    if (ReturnCode != 0)
    {
        Notify(LOCTEXT("AriadneTSTypeScriptDependenciesFailed", "Failed to install AriadneTS local TypeScript compiler. Check the Editor log."), 8.0f);
        UE_LOG(LogTemp, Error, TEXT("AriadneTS local TypeScript compiler install failed: %s\n%s"), *StdOut, *StdErr);
        return;
    }

    Notify(LOCTEXT("AriadneTSTypeScriptDependenciesInstalled", "AriadneTS local TypeScript compiler installed."));
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
        TEXT("--ts-root \"%s\" --version \"%s\" --build-number %d --private-key \"%s\" --output \"%s\" --required-abi 5"),
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

    const int32 Port = FMath::Clamp(Settings->DebugBasePort + Settings->DebugInstanceId, 1, 65535);
    const FString TypeScriptRoot = ResolveConfiguredPath(Settings->TypeScriptRoot);
    FString RelativeTypeScriptRoot = TypeScriptRoot;
    FPaths::MakePathRelativeTo(RelativeTypeScriptRoot, *FPaths::ProjectDir());
    RelativeTypeScriptRoot = RelativeTypeScriptRoot.Replace(TEXT("\\"), TEXT("/")).TrimStartAndEnd();
    if (RelativeTypeScriptRoot.IsEmpty() || RelativeTypeScriptRoot.StartsWith(TEXT("..")))
    {
        RelativeTypeScriptRoot = TEXT("TypeScript");
    }

    const FString ScriptPath = FPaths::ConvertRelativePathToFull(
        FPaths::Combine(PluginBaseDir(), TEXT("Scripts/upsert_vscode_launch_config.mjs")));
    const FString Args = FString::Printf(
        TEXT("--launch-json \"%s\" --host \"%s\" --port %d --ts-root \"${workspaceFolder}/%s\" --poll-interval-ms 250"),
        *LaunchJsonPath,
        *Settings->DebugHost.Replace(TEXT("\\"), TEXT("\\\\")).Replace(TEXT("\""), TEXT("\\\"")),
        Port,
        *RelativeTypeScriptRoot.Replace(TEXT("\\"), TEXT("/")).Replace(TEXT("\""), TEXT("\\\"")));

    int32 ReturnCode = 0;
    FString StdOut;
    FString StdErr;
    if (RunNodeScript(ScriptPath, Args, StdOut, StdErr, ReturnCode) && ReturnCode == 0)
    {
        Notify(LOCTEXT("AriadneTSVsCodeConfigCreated", "AriadneTS VSCode launch.json created or updated."));
    }
    else
    {
        Notify(LOCTEXT("AriadneTSVsCodeConfigFailed", "Failed to write AriadneTS VSCode launch.json."), 8.0f);
        UE_LOG(LogTemp, Error, TEXT("AriadneTS VSCode launch config update failed: %s\n%s"), *StdOut, *StdErr);
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
    Host->DebugStartupGraceMilliseconds = Settings->DebugStartupGraceMilliseconds;

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

FString FAriadneTSEditorModule::ResolveNpmExecutable() const
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString ConfiguredNpm = ResolveConfiguredPath(Settings->NpmExecutable);
    if (FPaths::FileExists(ConfiguredNpm))
    {
        return ConfiguredNpm;
    }

#if PLATFORM_WINDOWS
    const FString NpmExecutableName = TEXT("npm.cmd");
#else
    const FString NpmExecutableName = TEXT("npm");
#endif

    if (!Settings->NodeExecutable.IsEmpty())
    {
        const FString NodeDirectory = FPaths::GetPath(ResolveConfiguredPath(Settings->NodeExecutable));
        if (!NodeDirectory.IsEmpty())
        {
            const FString SiblingNpm = FPaths::Combine(NodeDirectory, NpmExecutableName);
            if (FPaths::FileExists(SiblingNpm))
            {
                return SiblingNpm;
            }
        }
    }

    return NpmExecutableName;
}

FString FAriadneTSEditorModule::ResolveNpmCliScript() const
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString ConfiguredNpm = ResolveConfiguredPath(Settings->NpmExecutable);
    if (ConfiguredNpm.EndsWith(TEXT("npm-cli.js")) && FPaths::FileExists(ConfiguredNpm))
    {
        return ConfiguredNpm;
    }
    const FString ConfiguredNpmDirectory = FPaths::GetPath(ConfiguredNpm);
    if (!ConfiguredNpmDirectory.IsEmpty())
    {
        const FString Candidates[] =
        {
            FPaths::Combine(ConfiguredNpmDirectory, TEXT("node_modules/npm/bin/npm-cli.js")),
            FPaths::ConvertRelativePathToFull(FPaths::Combine(ConfiguredNpmDirectory, TEXT("../lib/node_modules/npm/bin/npm-cli.js")))
        };
        for (const FString& Candidate : Candidates)
        {
            if (FPaths::FileExists(Candidate))
            {
                return Candidate;
            }
        }
    }

    if (Settings->NodeExecutable.IsEmpty())
    {
        return FString();
    }

    const FString NodeDirectory = FPaths::GetPath(ResolveConfiguredPath(Settings->NodeExecutable));
    if (NodeDirectory.IsEmpty())
    {
        return FString();
    }

    const FString Candidates[] =
    {
        FPaths::Combine(NodeDirectory, TEXT("node_modules/npm/bin/npm-cli.js")),
        FPaths::ConvertRelativePathToFull(FPaths::Combine(NodeDirectory, TEXT("../lib/node_modules/npm/bin/npm-cli.js")))
    };
    for (const FString& Candidate : Candidates)
    {
        if (FPaths::FileExists(Candidate))
        {
            return Candidate;
        }
    }

    return FString();
}

FString FAriadneTSEditorModule::ProjectNodeToolchainRoot() const
{
    return ResolveConfiguredPath(TEXT("{ProjectDir}/AriadneTS/Toolchain/node"));
}

FString FAriadneTSEditorModule::CreateNodeDownloadUrl(FString& OutArchiveFileName, bool& bOutIsZip) const
{
#if PLATFORM_WINDOWS
    const FString Platform = TEXT("win");
    bOutIsZip = true;
#else
    const FString Platform = TEXT("darwin");
    bOutIsZip = false;
#endif

#if PLATFORM_CPU_ARM_FAMILY
    const FString Arch = TEXT("arm64");
#else
    const FString Arch = TEXT("x64");
#endif

    const FString Extension = bOutIsZip ? TEXT("zip") : TEXT("tar.gz");
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    FString NormalizedVersion = Settings->NodeVersion.TrimStartAndEnd();
    if (NormalizedVersion.StartsWith(TEXT("v"), ESearchCase::IgnoreCase))
    {
        NormalizedVersion.RightChopInline(1);
    }
    if (NormalizedVersion.IsEmpty())
    {
        NormalizedVersion = TEXT("22.13.1");
    }

    OutArchiveFileName = FString::Printf(TEXT("node-v%s-%s-%s.%s"), *NormalizedVersion, *Platform, *Arch, *Extension);
    return FString::Printf(TEXT("https://nodejs.org/dist/v%s/%s"), *NormalizedVersion, *OutArchiveFileName);
}

bool FAriadneTSEditorModule::ExtractNodeArchive(
    const FString& ArchivePath,
    const FString& ExtractRoot,
    bool bIsZip,
    FString& OutError) const
{
    int32 ReturnCode = 0;
    FString StdOut;
    FString StdErr;
    if (bIsZip)
    {
        const FString Args = FString::Printf(
            TEXT("-NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '%s' -DestinationPath '%s' -Force\""),
            *ArchivePath.Replace(TEXT("'"), TEXT("''")),
            *ExtractRoot.Replace(TEXT("'"), TEXT("''")));
        FPlatformProcess::ExecProcess(TEXT("powershell.exe"), *Args, &ReturnCode, &StdOut, &StdErr);
    }
    else
    {
        const FString Args = FString::Printf(TEXT("-xzf \"%s\" -C \"%s\""), *ArchivePath, *ExtractRoot);
        FPlatformProcess::ExecProcess(TEXT("/usr/bin/tar"), *Args, &ReturnCode, &StdOut, &StdErr);
    }

    if (ReturnCode != 0)
    {
        OutError = StdOut + StdErr;
        return false;
    }

    return true;
}

bool FAriadneTSEditorModule::IsNpmCompatible(
    const FString& NpmCommand,
    const FString& NpmArguments,
    FString& OutReason) const
{
    const int32 NodeMajorVersion = ReadNodeMajorVersion();
    int32 ReturnCode = 0;
    FString StdOut;
    FString StdErr;
    if (NpmCommand.EndsWith(TEXT(".js")))
    {
        RunNodeScript(NpmCommand, NpmArguments, StdOut, StdErr, ReturnCode);
    }
    else
    {
        FPlatformProcess::ExecProcess(*NpmCommand, *NpmArguments, &ReturnCode, &StdOut, &StdErr);
    }

    const FString CombinedOutput = StdOut + StdErr;
    const int32 NpmMajorVersion = ParseMajorVersion(StdOut.TrimStartAndEnd());
    if (NodeMajorVersion >= 20 &&
        ((NpmMajorVersion > 0 && NpmMajorVersion < 10) ||
         CombinedOutput.Contains(TEXT("does not support Node.js"), ESearchCase::IgnoreCase)))
    {
        OutReason = FString::Printf(
            TEXT("The configured Node.js installation is using an npm version that is too old for it. Node major version: %d. npm: %s. npm output: %s. Recommended fixes: install a Node.js LTS or current release that bundles a modern npm, configure Project Settings > Plugins > AriadneTS > Node Executable to that node binary, or update npm for the configured Node installation."),
            NodeMajorVersion,
            *NpmCommand,
            *CombinedOutput.TrimStartAndEnd());
        return false;
    }

    return true;
}

int32 FAriadneTSEditorModule::ReadNodeMajorVersion() const
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString NodeExecutable = Settings->NodeExecutable.IsEmpty() ? FString(TEXT("node")) : ResolveConfiguredPath(Settings->NodeExecutable);
    int32 ReturnCode = 0;
    FString StdOut;
    FString StdErr;
    FPlatformProcess::ExecProcess(*NodeExecutable, TEXT("--version"), &ReturnCode, &StdOut, &StdErr);
    if (ReturnCode != 0)
    {
        return 0;
    }

    return ParseMajorVersion((StdOut + StdErr).TrimStartAndEnd());
}

int32 FAriadneTSEditorModule::ParseMajorVersion(const FString& VersionText)
{
    FString Digits;
    for (const TCHAR Character : VersionText)
    {
        if (FChar::IsDigit(Character))
        {
            Digits.AppendChar(Character);
            continue;
        }
        if (!Digits.IsEmpty())
        {
            break;
        }
    }

    return Digits.IsEmpty() ? 0 : FCString::Atoi(*Digits);
}

bool FAriadneTSEditorModule::RunNodeScript(
    const FString& ScriptPath,
    const FString& Arguments,
    FString& OutStdOut,
    FString& OutStdErr,
    int32& OutReturnCode) const
{
    const UAriadneTSEditorSettings* Settings = GetDefault<UAriadneTSEditorSettings>();
    const FString NodeExecutable = Settings->NodeExecutable.IsEmpty() ? FString(TEXT("node")) : ResolveConfiguredPath(Settings->NodeExecutable);
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
