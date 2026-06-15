using System;
using System.Collections.Generic;

namespace AriadneTS.Runtime
{
    public sealed class ScriptPackage
    {
        private readonly IReadOnlyDictionary<string, string> modules;

        internal ScriptPackage(
            ScriptPackageManifest manifest,
            IReadOnlyDictionary<string, string> modules)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
        }

        public ScriptPackageManifest Manifest { get; }

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
}

