using System;

namespace AriadneTS.Runtime
{
    [Serializable]
    public sealed class ScriptPackageManifest
    {
        public string Version;
        public long BuildNumber;
        public uint RequiredRuntimeAbiVersion;
        public string EntryModule;
        public ScriptPackageFile[] Files;
    }

    [Serializable]
    public sealed class ScriptPackageFile
    {
        public string Path;
        public long SizeBytes;
        public string Sha256;
    }
}
