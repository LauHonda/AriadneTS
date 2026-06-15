using System;
using System.Text;
using UnityEngine;

namespace AriadneTS.Runtime
{
    public static class UnityManifestSerializer
    {
        public static ScriptPackageManifest Deserialize(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            return JsonUtility.FromJson<ScriptPackageManifest>(Encoding.UTF8.GetString(bytes));
        }
    }
}

