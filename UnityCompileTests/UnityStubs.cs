using System;
using System.Collections;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }

        public static void Destroy(Object obj)
        {
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject?.transform;
    }

    public class MonoBehaviour : Component
    {
        public bool enabled { get; set; }

        public T GetComponent<T>() where T : class
        {
            return null;
        }
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name = "")
        {
            this.name = name;
            transform = new Transform();
            transform.gameObject = this;
        }

        public Transform transform { get; }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T();
            component.gameObject = this;
            return component;
        }
    }

    public sealed class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion localRotation { get; set; }

        public void SetParent(Transform parent, bool worldPositionStays)
        {
        }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }
    }

    public sealed class TextAsset : Object
    {
        public byte[] bytes { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TextAreaAttribute : Attribute
    {
        public TextAreaAttribute(int minLines, int maxLines)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrderAttribute : Attribute
    {
        public DefaultExecutionOrderAttribute(int order)
        {
        }
    }

    public static class Time
    {
        public static float deltaTime => 0;
    }

    public static class Debug
    {
        public static bool isDebugBuild => true;

        public static void Log(object message)
        {
        }

        public static void LogError(object message)
        {
        }

        public static void LogWarning(object message)
        {
        }

        public static void LogException(Exception exception)
        {
        }
    }

    public static class Application
    {
        public static bool runInBackground { get; set; }
        public static bool isEditor => true;
        public static string persistentDataPath => ".";
    }

    public static class JsonUtility
    {
        public static T FromJson<T>(string json)
        {
            return default;
        }
    }
}
