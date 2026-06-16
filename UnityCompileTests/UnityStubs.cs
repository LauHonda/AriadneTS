using System;
using System.Collections;

namespace UnityEngine
{
    public class Object
    {
    }

    public class MonoBehaviour : Object
    {
        public bool enabled { get; set; }

        public T GetComponent<T>() where T : class
        {
            return null;
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
