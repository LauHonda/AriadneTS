using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using AriadneTS.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AriadneTS.Editor
{
    public sealed class AriadneTsScriptToolsWindow : EditorWindow
    {
        private const string PackageName = "com.ariadnets.runtime";
        private const string VersionKey = "AriadneTS.ScriptTools.Version";
        private const string BuildNumberKey = "AriadneTS.ScriptTools.BuildNumber";
        private const string PrivateKeyKey = "AriadneTS.ScriptTools.PrivateKey";
        private const string OutputPathKey = "AriadneTS.ScriptTools.OutputPath";
        private const string NodePathKey = "AriadneTS.ScriptTools.NodePath";
        private const float BrowseButtonWidth = 80f;
        private const string RuntimeHostObjectName = "AriadneTS Runtime";

        private string version;
        private int buildNumber;
        private string privateKeyPath;
        private string outputPackagePath;
        private string nodeExecutable;
        private string publicKey;
        private string buildLog;
        private Vector2 scroll;

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string TypeScriptRoot =>
            Path.Combine(ProjectRoot, "TypeScript");

        private static string DefaultOutputPackagePath =>
            Path.Combine(ProjectRoot, "Assets", "TypeScript", "typescript-package.bytes");

        [MenuItem("Tools/AriadneTS/Script Tools")]
        public static void Open()
        {
            GetWindow<AriadneTsScriptToolsWindow>("AriadneTS Scripts");
        }

        private void OnEnable()
        {
            version = EditorPrefs.GetString(VersionKey, "0.2.0");
            buildNumber = EditorPrefs.GetInt(BuildNumberKey, 1);
            privateKeyPath = EditorPrefs.GetString(PrivateKeyKey, string.Empty);
            outputPackagePath = EditorPrefs.GetString(OutputPathKey, DefaultOutputPackagePath);
            nodeExecutable = EditorPrefs.GetString(NodePathKey, FindDefaultNodeExecutable());
            RefreshPublicKey();
        }

        private void OnGUI()
        {
            var scrollStarted = false;
            var verticalStarted = false;
            try
            {
                var contentWidth = Mathf.Max(260f, position.width - 28f);
                scroll = EditorGUILayout.BeginScrollView(scroll, false, true);
                scrollStarted = true;
                EditorGUILayout.BeginVertical(GUILayout.Width(contentWidth));
                verticalStarted = true;

                DrawProjectSection(contentWidth);
                EditorGUILayout.Space(12);
                DrawEnvironmentSection(contentWidth);
                EditorGUILayout.Space(12);
                DrawBuildSection(contentWidth);
                EditorGUILayout.Space(12);
                DrawSceneSection(contentWidth);
                EditorGUILayout.Space(12);
                DrawPublicKeySection(contentWidth);
                EditorGUILayout.Space(12);
                DrawLogSection(contentWidth);
            }
            catch (Exception exception)
            {
                buildLog = exception.ToString();
                UnityEngine.Debug.LogException(exception);
            }
            finally
            {
                if (verticalStarted)
                {
                    EditorGUILayout.EndVertical();
                }
                if (scrollStarted)
                {
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void OnFocus()
        {
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnProjectChange()
        {
            Repaint();
        }

        private void DrawProjectSection(float contentWidth)
        {
            EditorGUILayout.LabelField("Initialization", EditorStyles.boldLabel);
            DrawReadOnlyPath("Unity Project Root", ProjectRoot, contentWidth);
            DrawReadOnlyPath("TypeScript Root", TypeScriptRoot, contentWidth);

            var hasDirectory = Directory.Exists(TypeScriptRoot);
            var initialized = IsTypeScriptProjectInitialized();
            EditorGUILayout.LabelField(
                "Status",
                initialized ? "Initialized" : hasDirectory ? "Incomplete" : "Not Initialized");
            if (GUILayout.Button("Refresh State"))
            {
                AssetDatabase.Refresh();
                Repaint();
            }
            using (new EditorGUI.DisabledScope(initialized))
            {
                var buttonText = initialized
                    ? "Initialized"
                    : hasDirectory
                        ? "Repair TypeScript Project"
                        : "Initialize TypeScript Project";
                if (GUILayout.Button(buttonText))
                {
                    InitializeTypeScriptProject();
                }
            }

            if (initialized)
            {
                EditorGUILayout.HelpBox(
                    "TypeScript folder already exists at the Unity project root. Edit scripts there directly; initialization is disabled to avoid overwriting scripts.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Initialization creates or repairs editable default scripts under <UnityProject>/TypeScript/src without overwriting existing files.",
                    MessageType.Info);
            }
        }

        private void DrawBuildSection(float contentWidth)
        {
            EditorGUILayout.LabelField("Build Script Package", EditorStyles.boldLabel);
            version = TextFieldWithSave("Version", version, VersionKey);
            buildNumber = EditorGUILayout.IntField("Build Number", buildNumber);
            if (buildNumber < 0)
            {
                buildNumber = 0;
            }
            EditorPrefs.SetInt(BuildNumberKey, buildNumber);

            var previousNodeExecutable = nodeExecutable;
            DrawPathField(
                "Node Executable",
                ref nodeExecutable,
                NodePathKey,
                SelectNodeExecutable,
                contentWidth);
            if (nodeExecutable != previousNodeExecutable)
            {
                RefreshPublicKey();
            }

            var previousPrivateKeyPath = privateKeyPath;
            DrawPathField(
                "Private Key PEM",
                ref privateKeyPath,
                PrivateKeyKey,
                SelectPrivateKey,
                contentWidth);
            if (privateKeyPath != previousPrivateKeyPath)
            {
                RefreshPublicKey();
            }

            using (new EditorGUI.DisabledScope(!File.Exists(nodeExecutable)))
            {
                if (GUILayout.Button("Generate Development Private Key"))
                {
                    GenerateDevelopmentPrivateKey();
                }
            }
            DrawPathField(
                "Output .bytes",
                ref outputPackagePath,
                OutputPathKey,
                SelectOutputPackage,
                contentWidth);

            using (new EditorGUI.DisabledScope(!CanBuild()))
            {
                if (GUILayout.Button("Compile TypeScript And Build Package"))
                {
                    BuildPackage();
                }
            }

            if (!CanBuild())
            {
                EditorGUILayout.HelpBox(
                    "Build requires Node, an initialized TypeScript folder, an existing RSA private key, and an output path.",
                    MessageType.Warning);
            }
        }

        private void DrawEnvironmentSection(float contentWidth)
        {
            EditorGUILayout.LabelField("Environment", EditorStyles.boldLabel);
            DrawStatus("Node", File.Exists(nodeExecutable), string.IsNullOrWhiteSpace(nodeExecutable)
                ? "Not found"
                : nodeExecutable);
            DrawStatus("TypeScript Project", IsTypeScriptProjectInitialized(), TypeScriptRoot);
            DrawStatus("Private Key", File.Exists(privateKeyPath), string.IsNullOrWhiteSpace(privateKeyPath)
                ? "Not selected"
                : privateKeyPath);
            DrawStatus("Output Package", File.Exists(outputPackagePath), string.IsNullOrWhiteSpace(outputPackagePath)
                ? "Not built"
                : outputPackagePath);
            EditorGUILayout.HelpBox(
                "AriadneTS needs Node.js, a generated TypeScript project, a signing private key, and a built .bytes package. The build tool can use TypeScript from local node_modules or a compatible global install.",
                MessageType.None);
        }

        private void DrawSceneSection(float contentWidth)
        {
            EditorGUILayout.LabelField("Scene Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Create or update a GameObject with ScriptRuntimeHost, ScriptPackageRuntimeController, and ScriptPackageBootstrapper. The tool fills the public key and the output .bytes asset when they are available.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(publicKey)))
            {
                if (GUILayout.Button("Create Or Update Runtime Host In Scene"))
                {
                    CreateOrUpdateRuntimeHost();
                }
            }

            if (string.IsNullOrEmpty(publicKey))
            {
                EditorGUILayout.HelpBox(
                    "Select a private key first so the tool can fill the controller public key.",
                    MessageType.Warning);
            }
        }

        private void DrawPublicKeySection(float contentWidth)
        {
            EditorGUILayout.LabelField("Public Key", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The public key is derived from the private key. It does not change when TypeScript files or package contents change. It changes only when you use a different private key.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextArea(
                    publicKey ?? string.Empty,
                    GUILayout.Width(contentWidth),
                    GUILayout.MinHeight(70));
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(publicKey)))
            {
                if (GUILayout.Button("Copy Public Key"))
                {
                    EditorGUIUtility.systemCopyBuffer = publicKey;
                }
            }
        }

        private void DrawLogSection(float contentWidth)
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextArea(
                    buildLog ?? string.Empty,
                    GUILayout.Width(contentWidth),
                    GUILayout.MinHeight(120));
            }
        }

        private static void DrawStatus(string label, bool ok, string detail)
        {
            var previousColor = GUI.color;
            GUI.color = ok ? new Color(0.62f, 0.95f, 0.62f) : new Color(1f, 0.82f, 0.55f);
            EditorGUILayout.LabelField(label, ok ? "Ready" : "Missing");
            GUI.color = previousColor;
            using (new EditorGUI.DisabledScope(true))
            {
                var style = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true
                };
                EditorGUILayout.TextArea(detail ?? string.Empty, style, GUILayout.MinHeight(26));
            }
        }

        private static string TextFieldWithSave(string label, string value, string key)
        {
            var next = EditorGUILayout.TextField(label, value);
            if (next != value)
            {
                EditorPrefs.SetString(key, next);
            }
            return next;
        }

        private static void DrawPathField(
            string label,
            ref string value,
            string editorPrefsKey,
            Action selector,
            float contentWidth)
        {
            EditorGUILayout.LabelField(label);
            EditorGUILayout.BeginHorizontal();
            var textWidth = Mathf.Max(100f, contentWidth - BrowseButtonWidth - 8f);
            var style = new GUIStyle(EditorStyles.textField)
            {
                wordWrap = true
            };
            var next = EditorGUILayout.TextArea(
                value ?? string.Empty,
                style,
                GUILayout.Width(textWidth),
                GUILayout.MinHeight(36));
            if (next != value)
            {
                value = next;
                EditorPrefs.SetString(editorPrefsKey, value);
            }
            if (GUILayout.Button("Browse", GUILayout.Width(BrowseButtonWidth), GUILayout.Height(36)))
            {
                selector();
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawReadOnlyPath(string label, string value, float contentWidth)
        {
            EditorGUILayout.LabelField(label);
            using (new EditorGUI.DisabledScope(true))
            {
                var style = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true
                };
                EditorGUILayout.TextArea(
                    value ?? string.Empty,
                    style,
                    GUILayout.Width(contentWidth),
                    GUILayout.MinHeight(36));
            }
        }

        private void SelectPrivateKey()
        {
            var path = EditorUtility.OpenFilePanel("Select RSA private key", ProjectRoot, "pem");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            privateKeyPath = path;
            EditorPrefs.SetString(PrivateKeyKey, privateKeyPath);
            RefreshPublicKey();
        }

        private void SelectNodeExecutable()
        {
            var path = EditorUtility.OpenFilePanel("Select Node executable", "/usr/local/bin", string.Empty);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            nodeExecutable = path;
            EditorPrefs.SetString(NodePathKey, nodeExecutable);
            RefreshPublicKey();
        }

        private void SelectOutputPackage()
        {
            var path = EditorUtility.SaveFilePanel(
                "Output TypeScript Package",
                Path.GetDirectoryName(outputPackagePath) ?? ProjectRoot,
                "typescript-package",
                "bytes");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            outputPackagePath = path;
            EditorPrefs.SetString(OutputPathKey, outputPackagePath);
        }

        private void GenerateDevelopmentPrivateKey()
        {
            var path = EditorUtility.SaveFilePanel(
                "Generate AriadneTS Development Private Key",
                Path.Combine(ProjectRoot, "AriadneTSKeys"),
                "dev-private-key",
                "pem");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var script = Path.Combine(GetPackageRoot(), "Editor", "Tools", "build_script_package.mjs");
            try
            {
                var result = RunProcess(
                    nodeExecutable,
                    Quote(script) + " --generate-private-key --private-key " + Quote(path),
                    ProjectRoot);
                buildLog = result.CombinedOutput;
                if (result.ExitCode != 0)
                {
                    EditorUtility.DisplayDialog("AriadneTS Key Generation Failed", buildLog, "OK");
                    return;
                }

                privateKeyPath = path;
                EditorPrefs.SetString(PrivateKeyKey, privateKeyPath);
                RefreshPublicKey();
            }
            catch (Exception exception)
            {
                buildLog = exception.ToString();
                EditorUtility.DisplayDialog("AriadneTS Key Generation Failed", exception.Message, "OK");
            }
        }

        private void InitializeTypeScriptProject()
        {
            var templateRoot = Path.Combine(GetPackageRoot(), "Editor", "Templates", "TypeScript");
            CopyDirectory(templateRoot, TypeScriptRoot);
            Directory.CreateDirectory(Path.Combine(ProjectRoot, "Assets", "TypeScript"));
            AssetDatabase.Refresh();
            buildLog = "Initialized TypeScript project at:\n" + TypeScriptRoot;
            Repaint();
        }

        private bool CanBuild()
        {
            return !string.IsNullOrWhiteSpace(nodeExecutable) &&
                File.Exists(nodeExecutable) &&
                IsTypeScriptProjectInitialized() &&
                !string.IsNullOrWhiteSpace(privateKeyPath) &&
                File.Exists(privateKeyPath) &&
                !string.IsNullOrWhiteSpace(outputPackagePath);
        }

        private static bool IsTypeScriptProjectInitialized()
        {
            return Directory.Exists(TypeScriptRoot) &&
                File.Exists(Path.Combine(TypeScriptRoot, "package.json")) &&
                File.Exists(Path.Combine(TypeScriptRoot, "tsconfig.json")) &&
                Directory.Exists(Path.Combine(TypeScriptRoot, "src"));
        }

        private void RefreshPublicKey()
        {
            publicKey = string.Empty;
            if (string.IsNullOrWhiteSpace(privateKeyPath) || !File.Exists(privateKeyPath))
            {
                return;
            }

            var script = Path.Combine(GetPackageRoot(), "Editor", "Tools", "build_script_package.mjs");
            try
            {
                var result = RunProcess(
                    nodeExecutable,
                    Quote(script) + " --print-public-key --private-key " + Quote(privateKeyPath),
                    ProjectRoot);
                if (result.ExitCode == 0)
                {
                    publicKey = result.Stdout.Trim();
                }
                else
                {
                    buildLog = result.CombinedOutput;
                }
            }
            catch (Exception exception)
            {
                buildLog = exception.Message;
            }
        }

        private void BuildPackage()
        {
            var script = Path.Combine(GetPackageRoot(), "Editor", "Tools", "build_script_package.mjs");
            var arguments =
                Quote(script) +
                " --ts-root " + Quote(TypeScriptRoot) +
                " --version " + Quote(version) +
                " --build-number " + buildNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " --private-key " + Quote(privateKeyPath) +
                " --output " + Quote(outputPackagePath) +
                " --required-abi 4";

            try
            {
                var result = RunProcess(nodeExecutable, arguments, ProjectRoot);
                buildLog = result.CombinedOutput;
                if (result.ExitCode != 0)
                {
                    EditorUtility.DisplayDialog("AriadneTS Build Failed", buildLog, "OK");
                    return;
                }

                RefreshPublicKey();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("AriadneTS Build Complete", outputPackagePath, "OK");
            }
            catch (Exception exception)
            {
                buildLog = exception.ToString();
                EditorUtility.DisplayDialog("AriadneTS Build Failed", exception.Message, "OK");
            }
        }

        private void CreateOrUpdateRuntimeHost()
        {
            var hostObject = GameObject.Find(RuntimeHostObjectName);
            if (hostObject == null)
            {
                hostObject = new GameObject(RuntimeHostObjectName);
                Undo.RegisterCreatedObjectUndo(hostObject, "Create AriadneTS Runtime Host");
            }

            var runtimeHost = GetOrAddComponent<ScriptRuntimeHost>(hostObject);
            var controller = GetOrAddComponent<ScriptPackageRuntimeController>(hostObject);
            var bootstrapper = GetOrAddComponent<ScriptPackageBootstrapper>(hostObject);
            var demoBridge = GetOrAddComponent<AriadneTsDemoHostBridge>(hostObject);
            var actorBridge = GetOrAddComponent<AriadneActorBridge>(hostObject);
            var addressablesBridge = GetOrAddComponent(
                hostObject,
                "AriadneTS.Runtime.AriadneAddressablesBridge, AriadneTS.Addressables");
            var packageAsset = LoadTextAssetFromAbsolutePath(outputPackagePath);

            SetObjectReference(controller, "runtimeHost", runtimeHost);
            SetString(controller, "packageSigningPublicKey", publicKey);
            SetObjectReference(bootstrapper, "controller", controller);
            SetObjectReference(bootstrapper, "packageAsset", packageAsset);
            SetBool(bootstrapper, "startOnStart", packageAsset != null);
            SetObjectReference(demoBridge, "controller", controller);
            SetObjectReference(actorBridge, "controller", controller);
            if (addressablesBridge != null)
            {
                SetObjectReference(addressablesBridge, "controller", controller);
            }

            EditorUtility.SetDirty(hostObject);
            EditorSceneManager.MarkSceneDirty(hostObject.scene);
            Selection.activeObject = hostObject;
            buildLog = packageAsset == null
                ? "Runtime host was created, but the output package is not under Assets or has not been built yet. Assign the package TextAsset after building."
                : "Runtime host was created and configured:\n" + AssetDatabase.GetAssetPath(packageAsset);
            if (addressablesBridge == null)
            {
                buildLog += "\nAddressables bridge was not added. Ensure com.unity.addressables is installed and Unity has recompiled the package.";
            }
        }

        private static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(startInfo);
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, stdout, stderr);
        }

        private static string GetPackageRoot()
        {
            var guids = AssetDatabase.FindAssets("AriadneTsScriptToolsWindow t:Script");
            if (guids.Length == 0)
            {
                throw new InvalidOperationException("Could not locate AriadneTS editor package.");
            }

            var scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(scriptPath);
            if (package == null || string.IsNullOrEmpty(package.resolvedPath))
            {
                var packagePath = Path.Combine("Packages", PackageName, "package.json");
                package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath);
            }
            if (package == null || string.IsNullOrEmpty(package.resolvedPath))
            {
                throw new InvalidOperationException("Could not resolve AriadneTS package path.");
            }

            return package.resolvedPath;
        }

        private static T GetOrAddComponent<T>(GameObject hostObject)
            where T : Component
        {
            var component = hostObject.GetComponent<T>();
            return component != null
                ? component
                : Undo.AddComponent<T>(hostObject);
        }

        private static Component GetOrAddComponent(GameObject hostObject, string typeName)
        {
            var type = Type.GetType(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
            {
                return null;
            }

            var component = hostObject.GetComponent(type);
            return component != null
                ? component
                : Undo.AddComponent(hostObject, type);
        }

        private static TextAsset LoadTextAssetFromAbsolutePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return null;
            }

            var normalizedProjectRoot = Path.GetFullPath(ProjectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(absolutePath);
            if (!normalizedPath.StartsWith(normalizedProjectRoot, StringComparison.Ordinal))
            {
                return null;
            }

            var relativePath = normalizedPath.Substring(normalizedProjectRoot.Length)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            if (!relativePath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return null;
            }

            AssetDatabase.ImportAsset(relativePath);
            return AssetDatabase.LoadAssetAtPath<TextAsset>(relativePath);
        }

        private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static void SetString(UnityEngine.Object target, string propertyName, string value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.stringValue = value ?? string.Empty;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static void SetBool(UnityEngine.Object target, string propertyName, bool value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(directory.Replace(source, destination));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.Ordinal))
                {
                    continue;
                }
                var target = file.Replace(source, destination);
                if (!File.Exists(target))
                {
                    File.Copy(file, target, false);
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string FindDefaultNodeExecutable()
        {
            var candidates = new[]
            {
                "/opt/homebrew/bin/node",
                "/usr/local/bin/node",
                "/usr/bin/node",
                "C:\\Program Files\\nodejs\\node.exe",
                "C:\\Program Files (x86)\\nodejs\\node.exe",
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private readonly struct ProcessResult
        {
            public ProcessResult(int exitCode, string stdout, string stderr)
            {
                ExitCode = exitCode;
                Stdout = stdout;
                Stderr = stderr;
            }

            public int ExitCode { get; }
            public string Stdout { get; }
            public string Stderr { get; }
            public string CombinedOutput => Stdout + Stderr;
        }
    }
}
