using System.Collections.Generic;
using UnityEngine;

namespace AriadneTS.Runtime
{
    public sealed class AriadneScriptComponent : MonoBehaviour
    {
        [SerializeField]
        private int componentId;

        [SerializeField]
        private int componentType;

        private readonly Dictionary<string, string> properties =
            new Dictionary<string, string>(System.StringComparer.Ordinal);

        public int ComponentId => componentId;
        public int ComponentType => componentType;

        public void Configure(int id, int type)
        {
            componentId = id;
            componentType = type;
        }

        public void SetProperty(string property, string valueJson)
        {
            properties[property] = valueJson;
        }
    }
}
