using AriadneTS.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AriadneTS.Editor
{
    [InitializeOnLoad]
    internal static class AriadneTsDebugSettingsSynchronizer
    {
        private const string DebugEnabledKey = "AriadneTS.ScriptTools.Debug.Enabled";
        private const string DebugProtocolKey = "AriadneTS.ScriptTools.Debug.Protocol";
        private const string DebugHostKey = "AriadneTS.ScriptTools.Debug.Host";
        private const string DebugBasePortKey = "AriadneTS.ScriptTools.Debug.BasePort";
        private const string DebugInstanceIdKey = "AriadneTS.ScriptTools.Debug.InstanceId";
        private const string DebugRoleKey = "AriadneTS.ScriptTools.Debug.Role";
        private const string DebugWaitKey = "AriadneTS.ScriptTools.Debug.Wait";
        private const string DebugStartupGraceKey = "AriadneTS.ScriptTools.Debug.StartupGraceMs";

        static AriadneTsDebugSettingsSynchronizer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                ApplyToOpenScenes();
            }
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            _ = scene;
            _ = mode;
            ApplyToOpenScenes();
        }

        internal static int ApplyToOpenScenes()
        {
            if (!EditorPrefs.HasKey(DebugEnabledKey))
            {
                return 0;
            }

            var updated = 0;
            foreach (var runtimeHost in Resources.FindObjectsOfTypeAll<ScriptRuntimeHost>())
            {
                if (runtimeHost == null ||
                    runtimeHost.gameObject == null ||
                    !runtimeHost.gameObject.scene.IsValid() ||
                    !runtimeHost.gameObject.scene.isLoaded)
                {
                    continue;
                }

                Apply(runtimeHost);
                EditorUtility.SetDirty(runtimeHost);
                EditorSceneManager.MarkSceneDirty(runtimeHost.gameObject.scene);
                ++updated;
            }

            return updated;
        }

        private static void Apply(ScriptRuntimeHost runtimeHost)
        {
            var serializedObject = new SerializedObject(runtimeHost);
            SetBool(serializedObject, "enableScriptDebugging", EditorPrefs.GetBool(DebugEnabledKey, false));
            SetEnum(serializedObject, "debugProtocol", EditorPrefs.GetInt(DebugProtocolKey, (int)ScriptDebugProtocol.ChromeDevTools));
            SetString(serializedObject, "debugHost", EditorPrefs.GetString(DebugHostKey, "127.0.0.1"));
            SetInt(serializedObject, "debugBasePort", Mathf.Clamp(EditorPrefs.GetInt(DebugBasePortKey, 9229), 1, 65535));
            SetInt(serializedObject, "debugInstanceId", Mathf.Max(0, EditorPrefs.GetInt(DebugInstanceIdKey, 0)));
            SetString(serializedObject, "debugRole", EditorPrefs.GetString(DebugRoleKey, "Client"));
            SetBool(serializedObject, "waitForDebugger", EditorPrefs.GetBool(DebugWaitKey, false));
            SetInt(serializedObject, "debugStartupGraceMilliseconds", Mathf.Clamp(EditorPrefs.GetInt(DebugStartupGraceKey, 1000), 0, 5000));
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetEnum(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.enumValueIndex = value;
            }
        }

        private static void SetString(SerializedObject serializedObject, string propertyName, string value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.stringValue = value ?? string.Empty;
            }
        }

        private static void SetInt(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.intValue = value;
            }
        }
    }
}
