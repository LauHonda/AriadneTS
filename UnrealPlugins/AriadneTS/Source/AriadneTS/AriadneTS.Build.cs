using System.IO;
using UnrealBuildTool;

public class AriadneTS : ModuleRules
{
    public AriadneTS(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        CppStandard = CppStandardVersion.Cpp17;

        PublicDependencyModuleNames.AddRange(new[]
        {
            "Core",
            "CoreUObject",
            "Engine"
        });

        PrivateDependencyModuleNames.AddRange(new[]
        {
            "Json",
            "JsonUtilities",
            "Projects"
        });

        var NativeRoot = Path.Combine(ModuleDirectory, "..", "ThirdParty", "AriadneTSNative");
        PublicIncludePaths.Add(Path.Combine(NativeRoot, "Include"));

        if (Target.Platform == UnrealTargetPlatform.Mac)
        {
            var LibraryPath = Path.Combine(NativeRoot, "Lib", "Mac", "libariadnets.dylib");
            PublicDelayLoadDLLs.Add("libariadnets.dylib");
            RuntimeDependencies.Add("$(BinaryOutputDir)/libariadnets.dylib", LibraryPath);
        }
        else if (Target.Platform == UnrealTargetPlatform.Win64)
        {
            var DllPath = Path.Combine(NativeRoot, "Lib", "Win64", "ariadnets.dll");
            PublicDelayLoadDLLs.Add("ariadnets.dll");
            RuntimeDependencies.Add("$(BinaryOutputDir)/ariadnets.dll", DllPath);
        }
        else if (Target.Platform == UnrealTargetPlatform.Android)
        {
            PublicAdditionalLibraries.Add(Path.Combine(NativeRoot, "Lib", "Android", "arm64-v8a", "libariadnets.so"));
            PublicAdditionalLibraries.Add(Path.Combine(NativeRoot, "Lib", "Android", "x86_64", "libariadnets.so"));
        }
        else if (Target.Platform == UnrealTargetPlatform.IOS)
        {
            PublicAdditionalLibraries.Add(Path.Combine(NativeRoot, "Lib", "IOS", "libariadnets.a"));
        }
    }
}
