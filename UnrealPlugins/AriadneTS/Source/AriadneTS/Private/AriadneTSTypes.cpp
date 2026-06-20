#include "AriadneTSTypes.h"

FString FAriadneScriptPackage::LoadModule(const FString& ModuleName) const
{
    FString Normalized = ModuleName;
    Normalized.ReplaceInline(TEXT("\\"), TEXT("/"));
    if (Normalized.StartsWith(TEXT("./")))
    {
        Normalized.RightChopInline(2);
    }

    if (const FString* Source = Modules.Find(Normalized))
    {
        return *Source;
    }
    return FString();
}
