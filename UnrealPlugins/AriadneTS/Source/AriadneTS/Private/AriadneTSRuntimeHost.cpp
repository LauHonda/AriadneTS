#include "AriadneTSRuntimeHost.h"

#include "Dom/JsonObject.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "HAL/PlatformProcess.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

AAriadneTSRuntimeHost::AAriadneTSRuntimeHost()
{
    PrimaryActorTick.bCanEverTick = true;
}

void AAriadneTSRuntimeHost::BeginPlay()
{
    Super::BeginPlay();
    if (bStartOnBeginPlay)
    {
        FString ErrorText;
        if (!StartPackage(ErrorText))
        {
            UE_LOG(LogTemp, Error, TEXT("AriadneTS failed to start: %s"), *ErrorText);
        }
    }
}

void AAriadneTSRuntimeHost::Tick(float DeltaSeconds)
{
    Super::Tick(DeltaSeconds);
    if (!Runtime.IsCreated())
    {
        return;
    }

    FString Result;
    FString ErrorText;
    InvokeScript(
        TEXT("onTick"),
        FString::Printf(TEXT("{\"deltaTime\":%.9g}"), DeltaSeconds),
        Result,
        ErrorText);

    uint32 ExecutedJobs = 0;
    Runtime.ExecutePendingJobs(static_cast<uint32>(MaxJobsPerFrame), ExecutedJobs, ErrorText);
}

void AAriadneTSRuntimeHost::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
    FString Result;
    FString ErrorText;
    InvokeScript(TEXT("onEndPlay"), TEXT("null"), Result, ErrorText);
    StopPackage();
    Super::EndPlay(EndPlayReason);
}

bool AAriadneTSRuntimeHost::StartPackage(FString& OutError)
{
    StopPackage();
    ApplyDebugCommandLineOverrides();

    if (!FAriadneScriptPackageReader::ReadPackageFromFile(
        PackagePath,
        PackageSigningPublicKey,
        ActivePackage,
        OutError))
    {
        return false;
    }

    RegisterBuiltInHostHandlers();
    ReportDebugConfiguration();
    if (!Runtime.Create(
        FAriadneLogDelegate::CreateUObject(this, &AAriadneTSRuntimeHost::LogScriptMessage),
        FAriadneModuleLoadDelegate::CreateUObject(this, &AAriadneTSRuntimeHost::LoadModule),
        FAriadneHostInvokeDelegate::CreateUObject(this, &AAriadneTSRuntimeHost::InvokeHost),
        MemoryLimitBytes,
        MaxStackSizeBytes,
        static_cast<uint32>(ExecutionTimeoutMilliseconds),
        bEnableScriptDebugging,
        static_cast<uint32>(DebugProtocol),
        DebugHost.IsEmpty() ? FString(TEXT("127.0.0.1")) : DebugHost,
        static_cast<uint16>(GetDebugPort()),
        bWaitForDebugger,
        OutError))
    {
        return false;
    }

    const FString EntrySource = ActivePackage.LoadModule(ActivePackage.Manifest.EntryModule);
    if (EntrySource.IsEmpty())
    {
        OutError = TEXT("AriadneTS package entry module is missing.");
        return false;
    }

    if (bEnableScriptDebugging && !bWaitForDebugger && DebugStartupGraceMilliseconds > 0)
    {
        FPlatformProcess::Sleep(FMath::Min(DebugStartupGraceMilliseconds, 5000) / 1000.0f);
    }

    if (!Runtime.EvaluateModule(EntrySource, ActivePackage.Manifest.EntryModule, OutError))
    {
        return false;
    }

    FString Result;
    return InvokeScript(TEXT("onBeginPlay"), TEXT("null"), Result, OutError);
}

int32 AAriadneTSRuntimeHost::GetDebugPort() const
{
    return FMath::Clamp(DebugBasePort + DebugInstanceId, 1, 65535);
}

void AAriadneTSRuntimeHost::StopPackage()
{
    Runtime.Destroy();
    for (auto& Pair : ScriptActors)
    {
        if (Pair.Value.IsValid())
        {
            Pair.Value->Destroy();
        }
    }
    ScriptActors.Empty();
    ComponentActors.Empty();
}

bool AAriadneTSRuntimeHost::InvokeScript(
    const FString& Method,
    const FString& PayloadJson,
    FString& OutResult,
    FString& OutError)
{
    if (!Runtime.IsCreated())
    {
        OutError = TEXT("AriadneTS runtime is not running.");
        return false;
    }
    return Runtime.Invoke(Method, PayloadJson.IsEmpty() ? TEXT("null") : PayloadJson, OutResult, OutError);
}

void AAriadneTSRuntimeHost::RegisterBuiltInHostHandlers()
{
    HostHandlers.Empty();
    HostHandlers.Add(TEXT("ariadnets.log"), [this](const FString& Payload) { return HandleScriptLog(Payload); });

    const TArray<FString> NotImplementedMethods = {
        TEXT("ariadnets.async.begin"),
        TEXT("assets.verifySync"),
        TEXT("assets.loadSync"),
        TEXT("assets.loadGroupSync"),
        TEXT("assets.release"),
        TEXT("assets.releaseGroup"),
        TEXT("scenes.loadSync"),
        TEXT("scenes.unloadSync")
    };
    for (const FString& Method : NotImplementedMethods)
    {
        HostHandlers.Add(Method, [this, Method](const FString& Payload) { return HandleNotImplemented(Method, Payload); });
    }

    HostHandlers.Add(TEXT("actors.create"), [this](const FString& Payload) { return CreateScriptActor(Payload); });
    HostHandlers.Add(TEXT("actors.destroy"), [this](const FString& Payload) { return DestroyScriptActor(Payload); });
    HostHandlers.Add(TEXT("actors.setTransform"), [this](const FString& Payload) { return SetActorTransform(Payload); });
    HostHandlers.Add(TEXT("actors.setParent"), [this](const FString& Payload) { return SetActorParent(Payload); });
    HostHandlers.Add(TEXT("components.add"), [this](const FString& Payload) { return AddScriptComponent(Payload); });
    HostHandlers.Add(TEXT("components.remove"), [this](const FString& Payload) { return RemoveScriptComponent(Payload); });
    HostHandlers.Add(TEXT("components.setProperty"), [this](const FString& Payload) { return SetScriptComponentProperty(Payload); });
}

void AAriadneTSRuntimeHost::ApplyDebugCommandLineOverrides()
{
    const TCHAR* CommandLine = FCommandLine::Get();
    bEnableScriptDebugging = bEnableScriptDebugging || FParse::Param(CommandLine, TEXT("AriadneTSDebug"));
    bWaitForDebugger = bWaitForDebugger || FParse::Param(CommandLine, TEXT("AriadneTSWaitForDebugger"));

    FString TextValue;
    int32 IntValue = 0;
    if (FParse::Value(CommandLine, TEXT("AriadneTSDebugHost="), TextValue))
    {
        DebugHost = TextValue;
    }
    if (FParse::Value(CommandLine, TEXT("AriadneTSDebugRole="), TextValue))
    {
        DebugRole = TextValue;
    }
    if (FParse::Value(CommandLine, TEXT("AriadneTSDebugPort="), IntValue))
    {
        DebugBasePort = FMath::Clamp(IntValue, 1, 65535);
        DebugInstanceId = 0;
    }
    if (FParse::Value(CommandLine, TEXT("AriadneTSDebugBasePort="), IntValue))
    {
        DebugBasePort = FMath::Clamp(IntValue, 1, 65535);
    }
    if (FParse::Value(CommandLine, TEXT("AriadneTSDebugInstance="), IntValue))
    {
        DebugInstanceId = FMath::Max(0, IntValue);
    }
}

void AAriadneTSRuntimeHost::ReportDebugConfiguration() const
{
    if (!bEnableScriptDebugging)
    {
        return;
    }

    const TCHAR* ProtocolName = TEXT("None");
    if (DebugProtocol == EAriadneTSDebugProtocol::ChromeDevTools)
    {
        ProtocolName = TEXT("ChromeDevTools");
    }
    else if (DebugProtocol == EAriadneTSDebugProtocol::DebugAdapterProtocol)
    {
        ProtocolName = TEXT("DebugAdapterProtocol");
    }

    UE_LOG(
        LogTemp,
        Warning,
        TEXT("AriadneTS script debugging configured: protocol=%s host=%s port=%d role=%s waitForDebugger=%s. A TCP debug endpoint will listen on this address."),
        ProtocolName,
        *DebugHost,
        GetDebugPort(),
        *DebugRole,
        bWaitForDebugger ? TEXT("true") : TEXT("false"));
}

FString AAriadneTSRuntimeHost::LoadModule(const FString& ModuleName) const
{
    return ActivePackage.LoadModule(ModuleName);
}

FString AAriadneTSRuntimeHost::InvokeHost(const FString& Method, const FString& PayloadJson)
{
    if (FHostHandler* Handler = HostHandlers.Find(Method))
    {
        return (*Handler)(PayloadJson);
    }
    return Error(TEXT("UnknownHostMethod"), FString::Printf(TEXT("Unknown host method: %s"), *Method));
}

void AAriadneTSRuntimeHost::LogScriptMessage(const FString& Message) const
{
    UE_LOG(LogTemp, Log, TEXT("%s"), *Message);
}

FString AAriadneTSRuntimeHost::HandleScriptLog(const FString& PayloadJson) const
{
    TSharedPtr<FJsonObject> Object;
    if (!TryGetJsonObject(PayloadJson, Object))
    {
        return Ok();
    }

    const FString Level = Object->GetStringField(TEXT("level"));
    const FString Message = Object->GetStringField(TEXT("message"));
    if (Level == TEXT("error"))
    {
        UE_LOG(LogTemp, Error, TEXT("%s"), *Message);
    }
    else if (Level == TEXT("warning") || Level == TEXT("warn"))
    {
        UE_LOG(LogTemp, Warning, TEXT("%s"), *Message);
    }
    else
    {
        UE_LOG(LogTemp, Log, TEXT("%s"), *Message);
    }
    return Ok();
}

FString AAriadneTSRuntimeHost::HandleNotImplemented(const FString& Method, const FString& PayloadJson) const
{
    return Error(TEXT("NotImplemented"), FString::Printf(TEXT("Bridge method is not implemented: %s"), *Method));
}

FString AAriadneTSRuntimeHost::CreateScriptActor(const FString& PayloadJson)
{
    TSharedPtr<FJsonObject> Object;
    if (!TryGetJsonObject(PayloadJson, Object))
    {
        return Error(TEXT("InvalidRequest"), TEXT("Actor create payload is invalid."));
    }

    const int32 ActorId = Object->GetIntegerField(TEXT("id"));
    const FString Name = Object->GetStringField(TEXT("name"));
    UWorld* World = GetWorld();
    if (!World || ActorId <= 0)
    {
        return Error(TEXT("InvalidRequest"), TEXT("World and actor id are required."));
    }

    FActorSpawnParameters SpawnParameters;
    SpawnParameters.Name = MakeUniqueObjectName(World, AActor::StaticClass(), FName(*Name));
    AActor* Actor = World->SpawnActor<AActor>(AActor::StaticClass(), FTransform::Identity, SpawnParameters);
    if (!Actor)
    {
        return Error(TEXT("SpawnFailed"), TEXT("Failed to spawn Unreal actor."));
    }
#if WITH_EDITOR
    Actor->SetActorLabel(Name);
#endif
    ScriptActors.Add(ActorId, Actor);

    Actor->SetActorLocation(JsonVector(Object, TEXT("position")));
    Actor->SetActorQuat(JsonQuat(Object, TEXT("rotation")));

    if (Object->GetBoolField(TEXT("hasParent")))
    {
        AActor* ParentActor = nullptr;
        if (TryGetScriptActor(Object->GetIntegerField(TEXT("parentId")), ParentActor))
        {
            Actor->AttachToActor(ParentActor, FAttachmentTransformRules::KeepWorldTransform);
        }
    }

    return Ok(FString::Printf(TEXT("{\"id\":%d,\"name\":\"%s\"}"), ActorId, *Name));
}

FString AAriadneTSRuntimeHost::DestroyScriptActor(const FString& PayloadJson)
{
    TSharedPtr<FJsonObject> Object;
    if (!TryGetJsonObject(PayloadJson, Object))
    {
        return Error(TEXT("InvalidRequest"), TEXT("Actor destroy payload is invalid."));
    }

    const int32 ActorId = Object->GetIntegerField(TEXT("id"));
    if (TWeakObjectPtr<AActor>* Actor = ScriptActors.Find(ActorId))
    {
        if (Actor->IsValid())
        {
            Actor->Get()->Destroy();
        }
        ScriptActors.Remove(ActorId);
    }
    return Ok();
}

FString AAriadneTSRuntimeHost::SetActorTransform(const FString& PayloadJson)
{
    TSharedPtr<FJsonObject> Object;
    if (!TryGetJsonObject(PayloadJson, Object))
    {
        return Error(TEXT("InvalidRequest"), TEXT("Actor transform payload is invalid."));
    }

    AActor* Actor = nullptr;
    if (!TryGetScriptActor(Object->GetIntegerField(TEXT("id")), Actor))
    {
        return Error(TEXT("ActorNotFound"), TEXT("Actor does not exist."));
    }

    const FString Property = Object->GetStringField(TEXT("property"));
    const TSharedPtr<FJsonObject> Value = Object->GetObjectField(TEXT("value"));
    if (Property == TEXT("position"))
    {
        Actor->SetActorLocation(JsonVector(Value, TEXT("")));
    }
    else if (Property == TEXT("rotation"))
    {
        Actor->SetActorQuat(JsonQuat(Value, TEXT("")));
    }
    else if (Property == TEXT("localPosition"))
    {
        Actor->SetActorRelativeLocation(JsonVector(Value, TEXT("")));
    }
    else if (Property == TEXT("localRotation"))
    {
        Actor->SetActorRelativeRotation(JsonQuat(Value, TEXT("")).Rotator());
    }
    return Ok();
}

FString AAriadneTSRuntimeHost::SetActorParent(const FString& PayloadJson)
{
    TSharedPtr<FJsonObject> Object;
    if (!TryGetJsonObject(PayloadJson, Object))
    {
        return Error(TEXT("InvalidRequest"), TEXT("Actor parent payload is invalid."));
    }

    AActor* Actor = nullptr;
    if (!TryGetScriptActor(Object->GetIntegerField(TEXT("id")), Actor))
    {
        return Error(TEXT("ActorNotFound"), TEXT("Actor does not exist."));
    }

    if (Object->GetBoolField(TEXT("hasParent")))
    {
        AActor* Parent = nullptr;
        if (!TryGetScriptActor(Object->GetIntegerField(TEXT("parentId")), Parent))
        {
            return Error(TEXT("ParentNotFound"), TEXT("Parent actor does not exist."));
        }
        Actor->AttachToActor(Parent, FAttachmentTransformRules::KeepWorldTransform);
    }
    else
    {
        Actor->DetachFromActor(FDetachmentTransformRules::KeepWorldTransform);
    }
    return Ok();
}

FString AAriadneTSRuntimeHost::AddScriptComponent(const FString& PayloadJson)
{
    TSharedPtr<FJsonObject> Object;
    if (!TryGetJsonObject(PayloadJson, Object))
    {
        return Error(TEXT("InvalidRequest"), TEXT("Component add payload is invalid."));
    }
    ComponentActors.Add(Object->GetIntegerField(TEXT("componentId")), Object->GetIntegerField(TEXT("actorId")));
    return Ok(FString::Printf(
        TEXT("{\"id\":%d,\"type\":%d}"),
        Object->GetIntegerField(TEXT("componentId")),
        Object->GetIntegerField(TEXT("type"))));
}

FString AAriadneTSRuntimeHost::RemoveScriptComponent(const FString& PayloadJson)
{
    TSharedPtr<FJsonObject> Object;
    if (TryGetJsonObject(PayloadJson, Object))
    {
        ComponentActors.Remove(Object->GetIntegerField(TEXT("componentId")));
    }
    return Ok();
}

FString AAriadneTSRuntimeHost::SetScriptComponentProperty(const FString& PayloadJson)
{
    return Ok();
}

bool AAriadneTSRuntimeHost::TryGetJsonObject(const FString& PayloadJson, TSharedPtr<FJsonObject>& OutObject) const
{
    const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(PayloadJson.IsEmpty() ? TEXT("{}") : PayloadJson);
    return FJsonSerializer::Deserialize(Reader, OutObject) && OutObject.IsValid();
}

bool AAriadneTSRuntimeHost::TryGetScriptActor(int32 ActorId, AActor*& OutActor) const
{
    OutActor = nullptr;
    const TWeakObjectPtr<AActor>* Found = ScriptActors.Find(ActorId);
    if (!Found || !Found->IsValid())
    {
        return false;
    }
    OutActor = Found->Get();
    return true;
}

FVector AAriadneTSRuntimeHost::JsonVector(const TSharedPtr<FJsonObject>& Object, const TCHAR* FieldName, const FVector& DefaultValue)
{
    TSharedPtr<FJsonObject> Source = Object;
    if (FieldName && FCString::Strlen(FieldName) > 0 && Object.IsValid() && Object->HasTypedField<EJson::Object>(FieldName))
    {
        Source = Object->GetObjectField(FieldName);
    }
    if (!Source.IsValid())
    {
        return DefaultValue;
    }
    return FVector(
        Source->GetNumberField(TEXT("x")),
        Source->GetNumberField(TEXT("y")),
        Source->GetNumberField(TEXT("z")));
}

FQuat AAriadneTSRuntimeHost::JsonQuat(const TSharedPtr<FJsonObject>& Object, const TCHAR* FieldName, const FQuat& DefaultValue)
{
    TSharedPtr<FJsonObject> Source = Object;
    if (FieldName && FCString::Strlen(FieldName) > 0 && Object.IsValid() && Object->HasTypedField<EJson::Object>(FieldName))
    {
        Source = Object->GetObjectField(FieldName);
    }
    if (!Source.IsValid())
    {
        return DefaultValue;
    }
    return FQuat(
        Source->GetNumberField(TEXT("x")),
        Source->GetNumberField(TEXT("y")),
        Source->GetNumberField(TEXT("z")),
        Source->GetNumberField(TEXT("w")));
}

FString AAriadneTSRuntimeHost::Ok(const FString& ResultJson)
{
    return FString::Printf(TEXT("{\"ok\":true,\"result\":%s}"), *ResultJson);
}

FString AAriadneTSRuntimeHost::Error(const FString& Code, const FString& Message)
{
    return FString::Printf(
        TEXT("{\"ok\":false,\"error\":{\"code\":\"%s\",\"message\":\"%s\"}}"),
        *Code.Replace(TEXT("\""), TEXT("\\\"")),
        *Message.Replace(TEXT("\""), TEXT("\\\"")));
}
