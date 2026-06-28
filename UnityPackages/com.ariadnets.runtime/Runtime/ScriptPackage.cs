using System;
using System.Collections.Generic;

namespace AriadneTS.Runtime
{
    public sealed class ScriptPackage
    {
        private const string DebugMetadataPath = "debug-metadata.json";
        private readonly IReadOnlyDictionary<string, string> modules;

        internal ScriptPackage(
            ScriptPackageManifest manifest,
            IReadOnlyDictionary<string, string> modules)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
            DebugMetadataJson = LoadModule(DebugMetadataPath);
        }

        public ScriptPackageManifest Manifest { get; }
        public string DebugMetadataJson { get; }

        public string LoadModule(string moduleName)
        {
            if (moduleName == null)
            {
                return null;
            }

            return modules.TryGetValue(NormalizePath(moduleName), out var source)
                ? source
                : null;
        }

        internal static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

    }

    [Serializable]
    public sealed class ScriptPackageDebugMetadata
    {
        public int SchemaVersion;
        public string PackageVersion;
        public long BuildNumber;
        public string EntryModule;
        public ScriptPackageDebugModule[] Modules;
        public ScriptPackageDebugProbe[] Probes;

        public string FindSourceMapPath(string module)
        {
            if (Modules == null || string.IsNullOrEmpty(module))
            {
                return null;
            }

            var normalized = ScriptPackage.NormalizePath(module);
            foreach (var item in Modules)
            {
                if (item != null &&
                    string.Equals(item.Path, normalized, StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(item.SourceMapPath))
                {
                    return item.SourceMapPath;
                }
            }
            return null;
        }
    }

    [Serializable]
    public sealed class ScriptPackageDebugModule
    {
        public string Path;
        public string SourceMapPath;
        public bool DynamicBreakpoints;
    }

    [Serializable]
    public sealed class ScriptPackageDebugProbe
    {
        public int Id;
        public string Kind;
        public string Module;
        public int GeneratedLine;
        public string Source;
        public int Line;
        public int Column;
        public string Function;
    }
}
