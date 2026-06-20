using UnrealBuildTool;

public class AriadneTSEditor : ModuleRules
{
    public AriadneTSEditor(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        CppStandard = CppStandardVersion.Cpp17;

        PublicDependencyModuleNames.AddRange(new[]
        {
            "Core",
            "CoreUObject",
            "Engine",
            "AriadneTS"
        });

        PrivateDependencyModuleNames.AddRange(new[]
        {
            "DeveloperSettings",
            "EditorStyle",
            "LevelEditor",
            "Projects",
            "Slate",
            "SlateCore",
            "ToolMenus",
            "UnrealEd"
        });
    }
}
