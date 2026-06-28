using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
        private const string DefaultNodeVersion = "22.13.1";
        private const string NodeVersionKey = "AriadneTS.ScriptTools.NodeVersion";
        private const string NodeDistributionIndexUrl = "https://nodejs.org/dist/index.json";
        private const int MaxNodeVersionMenuItems = 80;
        private const string DebugEnabledKey = "AriadneTS.ScriptTools.Debug.Enabled";
        private const string DebugProtocolKey = "AriadneTS.ScriptTools.Debug.Protocol";
        private const string DebugHostKey = "AriadneTS.ScriptTools.Debug.Host";
        private const string DebugBasePortKey = "AriadneTS.ScriptTools.Debug.BasePort";
        private const string DebugInstanceIdKey = "AriadneTS.ScriptTools.Debug.InstanceId";
        private const string DebugRoleKey = "AriadneTS.ScriptTools.Debug.Role";
        private const string DebugWaitKey = "AriadneTS.ScriptTools.Debug.Wait";
        private const string DebugStartupGraceKey = "AriadneTS.ScriptTools.Debug.StartupGraceMs";
        private const float BrowseButtonWidth = 80f;
        private const string RuntimeHostObjectName = "AriadneTS Runtime";

        private string version;
        private int buildNumber;
        private string privateKeyPath;
        private string outputPackagePath;
        private string nodeVersion;
        private string nodeExecutable;
        private string npmExecutable;
        private string publicKey;
        private string buildLog;
        private Vector2 scroll;
        private bool debugEnabled;
        private ScriptDebugProtocol debugProtocol;
        private string debugHost;
        private int debugBasePort;
        private int debugInstanceId;
        private string debugRole;
        private bool debugWaitForAttach;
        private int debugStartupGraceMilliseconds;

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string TypeScriptRoot =>
            Path.Combine(ProjectRoot, "TypeScript");

        private static string DefaultOutputPackagePath =>
            Path.Combine(ProjectRoot, "Assets", "typescript-package.bytes");

        private static string LegacyDefaultOutputPackagePath =>
            Path.Combine(ProjectRoot, "Assets", "TypeScript", "typescript-package.bytes");

        private static string VsCodeLaunchJsonPath =>
            Path.Combine(ProjectRoot, ".vscode", "launch.json");

        private static string DefaultProjectNodeExecutable =>
            Application.platform == RuntimePlatform.WindowsEditor
                ? Path.Combine(ProjectRoot, "AriadneTS", "Toolchain", "node", "node.exe")
                : Path.Combine(ProjectRoot, "AriadneTS", "Toolchain", "node", "bin", "node");

        private static string DefaultProjectNpmExecutable =>
            Application.platform == RuntimePlatform.WindowsEditor
                ? Path.Combine(ProjectRoot, "AriadneTS", "Toolchain", "node", "npm.cmd")
                : Path.Combine(ProjectRoot, "AriadneTS", "Toolchain", "node", "bin", "npm");

        private static string ProjectToolchainNodeRoot =>
            Path.Combine(ProjectRoot, "AriadneTS", "Toolchain", "node");

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
            nodeVersion = EditorPrefs.GetString(NodeVersionKey, DefaultNodeVersion);
            outputPackagePath = EditorPrefs.GetString(OutputPathKey, DefaultOutputPackagePath);
            if (Path.GetFullPath(outputPackagePath) == Path.GetFullPath(LegacyDefaultOutputPackagePath))
            {
                outputPackagePath = DefaultOutputPackagePath;
                EditorPrefs.SetString(OutputPathKey, outputPackagePath);
            }
            nodeExecutable = DefaultProjectNodeExecutable;
            npmExecutable = DefaultProjectNpmExecutable;
            debugEnabled = EditorPrefs.GetBool(DebugEnabledKey, false);
            debugProtocol = (ScriptDebugProtocol)EditorPrefs.GetInt(DebugProtocolKey, (int)ScriptDebugProtocol.ChromeDevTools);
            debugHost = EditorPrefs.GetString(DebugHostKey, "127.0.0.1");
            debugBasePort = EditorPrefs.GetInt(DebugBasePortKey, 9229);
            debugInstanceId = EditorPrefs.GetInt(DebugInstanceIdKey, 0);
            debugRole = EditorPrefs.GetString(DebugRoleKey, "Client");
            debugWaitForAttach = EditorPrefs.GetBool(DebugWaitKey, false);
            debugStartupGraceMilliseconds = EditorPrefs.GetInt(DebugStartupGraceKey, 1000);
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

                DrawEnvironmentSection(contentWidth);
                EditorGUILayout.Space(12);
                DrawBuildSection(contentWidth);
                EditorGUILayout.Space(12);
                DrawRuntimeSection();
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

        private void DrawEnvironmentSection(float contentWidth)
        {
            EditorGUILayout.LabelField("Environment Setup", EditorStyles.boldLabel);
            DrawReadOnlyPath("Unity Project Root", ProjectRoot, contentWidth);
            DrawReadOnlyPath("TypeScript Root", TypeScriptRoot, contentWidth);

            var hasDirectory = Directory.Exists(TypeScriptRoot);
            var initialized = IsTypeScriptProjectInitialized();
            DrawStatus("Node", File.Exists(nodeExecutable), string.IsNullOrWhiteSpace(nodeExecutable)
                ? "Not found"
                : nodeExecutable);
            DrawStatus("Target Node Version", true, NormalizeNodeVersion(nodeVersion));
            DrawStatus("npm", HasUsableNpm(), NpmStatusText());
            DrawStatus("TypeScript Project", initialized, initialized
                ? TypeScriptRoot
                : hasDirectory ? "Incomplete: " + TypeScriptRoot : TypeScriptRoot);
            DrawStatus("Local TypeScript Compiler", AreTypeScriptDependenciesInstalled(), TypeScriptDependenciesStatusText());
            DrawStatus("VSCode Debug Config", IsVsCodeDebugConfigInitialized(), VsCodeLaunchJsonPath);

            DrawReadOnlyPath("Project Node Executable", nodeExecutable, contentWidth);
            DrawReadOnlyPath("Project npm Executable", npmExecutable, contentWidth);
            EditorGUILayout.LabelField("Target Node Version", "v" + NormalizeNodeVersion(nodeVersion));
            if (GUILayout.Button("Select Node.js Version"))
            {
                ShowNodeVersionMenu();
            }

            if (GUILayout.Button("Refresh State"))
            {
                AssetDatabase.Refresh();
                Repaint();
            }
            if (GUILayout.Button("Install/Change Project Node.js Toolchain"))
            {
                InstallProjectNodeToolchain();
            }
            if (GUILayout.Button("Diagnose TypeScript Environment"))
            {
                DiagnoseTypeScriptEnvironment();
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
            using (new EditorGUI.DisabledScope(!File.Exists(nodeExecutable) || !HasUsableNpm() || !File.Exists(Path.Combine(TypeScriptRoot, "package.json"))))
            {
                if (GUILayout.Button("Install Local TypeScript Compiler"))
                {
                    InstallTypeScriptDependencies();
                }
            }
            using (new EditorGUI.DisabledScope(!File.Exists(nodeExecutable)))
            {
                if (GUILayout.Button("Install VSCode AriadneTS Debugger And Config"))
                {
                    InstallVsCodeDebuggerExtension();
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
            EditorGUILayout.HelpBox(
                "AriadneTS needs Node.js for editor build tools. The local TypeScript compiler is a project build dependency used to compile scripts before packaging; it is not required by the game runtime.",
                MessageType.None);
        }

        private void DrawBuildSection(float contentWidth)
        {
            EditorGUILayout.LabelField("Package Signing And Build", EditorStyles.boldLabel);
            version = TextFieldWithSave("Version", version, VersionKey);
            buildNumber = EditorGUILayout.IntField("Build Number", buildNumber);
            if (buildNumber < 0)
            {
                buildNumber = 0;
            }
            EditorPrefs.SetInt(BuildNumberKey, buildNumber);

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
            DrawStatus("Private Key", File.Exists(privateKeyPath), string.IsNullOrWhiteSpace(privateKeyPath)
                ? "Not selected"
                : privateKeyPath);

            EditorGUILayout.LabelField("Public Key");
            EditorGUILayout.HelpBox(
                "The public key is derived from the private key. Configure this public key on runtime controllers; keep the private key out of source control.",
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

            DrawPathField(
                "Output .bytes",
                ref outputPackagePath,
                OutputPathKey,
                SelectOutputPackage,
                contentWidth);
            DrawStatus("Output Package", File.Exists(outputPackagePath), string.IsNullOrWhiteSpace(outputPackagePath)
                ? "Not built"
                : outputPackagePath);

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

        private void DrawRuntimeSection()
        {
            EditorGUILayout.LabelField("Runtime And Debugging", EditorStyles.boldLabel);
            var changed = false;
            var nextEnabled = EditorGUILayout.Toggle("Enable Debugging", debugEnabled);
            if (nextEnabled != debugEnabled)
            {
                debugEnabled = nextEnabled;
                EditorPrefs.SetBool(DebugEnabledKey, debugEnabled);
                changed = true;
            }

            var nextProtocol = (ScriptDebugProtocol)EditorGUILayout.EnumPopup("Protocol", debugProtocol);
            if (nextProtocol != debugProtocol)
            {
                debugProtocol = nextProtocol;
                EditorPrefs.SetInt(DebugProtocolKey, (int)debugProtocol);
                changed = true;
            }

            var nextHost = EditorGUILayout.TextField("Host", debugHost);
            if (nextHost != debugHost)
            {
                debugHost = nextHost;
                EditorPrefs.SetString(DebugHostKey, debugHost);
                changed = true;
            }

            var nextBasePort = EditorGUILayout.IntField("Base Port", debugBasePort);
            var clampedBasePort = Mathf.Clamp(nextBasePort, 1, 65535);
            if (clampedBasePort != debugBasePort)
            {
                debugBasePort = clampedBasePort;
                EditorPrefs.SetInt(DebugBasePortKey, debugBasePort);
                changed = true;
            }

            var nextInstanceId = EditorGUILayout.IntField("Instance Id", debugInstanceId);
            var clampedInstanceId = Mathf.Max(0, nextInstanceId);
            if (clampedInstanceId != debugInstanceId)
            {
                debugInstanceId = clampedInstanceId;
                EditorPrefs.SetInt(DebugInstanceIdKey, debugInstanceId);
                changed = true;
            }

            var nextRole = EditorGUILayout.TextField("Role", debugRole);
            if (nextRole != debugRole)
            {
                debugRole = nextRole;
                EditorPrefs.SetString(DebugRoleKey, debugRole);
                changed = true;
            }

            var nextWait = EditorGUILayout.Toggle("Wait For Debugger", debugWaitForAttach);
            if (nextWait != debugWaitForAttach)
            {
                debugWaitForAttach = nextWait;
                EditorPrefs.SetBool(DebugWaitKey, debugWaitForAttach);
                changed = true;
            }

            var nextStartupGrace = EditorGUILayout.IntField("Startup Grace Ms", debugStartupGraceMilliseconds);
            var clampedStartupGrace = Mathf.Clamp(nextStartupGrace, 0, 5000);
            if (clampedStartupGrace != debugStartupGraceMilliseconds)
            {
                debugStartupGraceMilliseconds = clampedStartupGrace;
                EditorPrefs.SetInt(DebugStartupGraceKey, debugStartupGraceMilliseconds);
                changed = true;
            }

            var actualPort = Mathf.Clamp(debugBasePort + debugInstanceId, 1, 65535);
            EditorGUILayout.LabelField("Actual Port", actualPort.ToString(CultureInfo.InvariantCulture));
            if (changed)
            {
                SyncDebugConfiguration();
            }
            if (GUILayout.Button("Apply Debug Settings To Scene And VSCode"))
            {
                SyncDebugConfiguration();
            }
            EditorGUILayout.HelpBox(
                "This config is the source of truth for the open scene RuntimeHost and .vscode/launch.json. Startup Grace gives VSCode time to apply onBeginPlay breakpoints when Wait For Debugger is off. Use a unique Instance Id per Unity/Unreal client or server process.",
                MessageType.Info);

            EditorGUILayout.Space(6);
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

        private void ShowNodeVersionMenu()
        {
            try
            {
                var versions = FetchNodeVersions();
                if (versions.Length == 0)
                {
                    EditorUtility.DisplayDialog(
                        "AriadneTS Node.js Versions",
                        "No downloadable Node.js versions were found in the Node.js distribution index.",
                        "OK");
                    return;
                }

                var menu = new GenericMenu();
                var currentVersion = NormalizeNodeVersion(nodeVersion);
                var latestLts = FindLatestLtsVersion(versions);
                if (!string.IsNullOrWhiteSpace(latestLts.Version))
                {
                    AddNodeVersionMenuItem(menu, "Recommended LTS/" + latestLts.DisplayLabel, latestLts, currentVersion);
                }
                AddNodeVersionMenuItem(menu, "Latest Current/" + versions[0].DisplayLabel, versions[0], currentVersion);
                menu.AddSeparator(string.Empty);

                var count = Math.Min(MaxNodeVersionMenuItems, versions.Length);
                for (var index = 0; index < count; ++index)
                {
                    var candidate = versions[index];
                    var group = candidate.IsLts ? "LTS" : "Current";
                    AddNodeVersionMenuItem(menu, group + "/" + candidate.DisplayLabel, candidate, currentVersion);
                }

                menu.ShowAsContext();
            }
            catch (Exception exception)
            {
                buildLog = exception.ToString();
                EditorUtility.DisplayDialog("AriadneTS Node.js Version List Failed", exception.Message, "OK");
            }
        }

        private void AddNodeVersionMenuItem(
            GenericMenu menu,
            string label,
            NodeVersionInfo versionInfo,
            string currentVersion)
        {
            var selectedVersion = versionInfo.Version;
            menu.AddItem(
                new GUIContent(label),
                string.Equals(selectedVersion, currentVersion, StringComparison.Ordinal),
                () =>
                {
                    nodeVersion = selectedVersion;
                    EditorPrefs.SetString(NodeVersionKey, nodeVersion);
                    buildLog = "Selected Node.js version: v" + nodeVersion;
                    Repaint();
                });
        }

        private static NodeVersionInfo FindLatestLtsVersion(NodeVersionInfo[] versions)
        {
            foreach (var versionInfo in versions)
            {
                if (versionInfo.IsLts)
                {
                    return versionInfo;
                }
            }

            return default;
        }

        private static NodeVersionInfo[] FetchNodeVersions()
        {
            string json;
            using (var client = new WebClient())
            {
                json = client.DownloadString(NodeDistributionIndexUrl);
            }

            var versions = new System.Collections.Generic.List<NodeVersionInfo>();
            foreach (Match objectMatch in Regex.Matches(json, "\\{[^{}]*\\}"))
            {
                var entry = objectMatch.Value;
                var versionText = ReadJsonString(entry, "version");
                if (string.IsNullOrWhiteSpace(versionText))
                {
                    continue;
                }

                var normalizedVersion = NormalizeNodeVersion(versionText);
                if (ParseMajorVersion(normalizedVersion) < 18)
                {
                    continue;
                }

                var ltsText = ReadJsonValue(entry, "lts");
                var isLts = !string.Equals(ltsText, "false", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(ltsText);
                versions.Add(new NodeVersionInfo(
                    normalizedVersion,
                    ReadJsonString(entry, "date"),
                    ReadJsonString(entry, "npm"),
                    isLts ? ltsText.Trim('"') : string.Empty));
            }

            return versions.ToArray();
        }

        private static string ReadJsonString(string jsonObject, string propertyName)
        {
            var match = Regex.Match(jsonObject, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ReadJsonValue(string jsonObject, string propertyName)
        {
            var match = Regex.Match(jsonObject, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*(false|\"[^\"]*\")");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private void InstallProjectNodeToolchain()
        {
            if (!EditorUtility.DisplayDialog(
                    "Install/Change AriadneTS Project Node.js Toolchain",
                    "This downloads Node.js " + NormalizeNodeVersion(nodeVersion) +
                    " into this project under AriadneTS/Toolchain/node. The toolchain folder is ignored by git.",
                    "Install",
                    "Cancel"))
            {
                return;
            }

            var archivePath = string.Empty;
            var extractRoot = string.Empty;
            var tempRoot = string.Empty;
            try
            {
                var download = CreateNodeDownloadInfo(NormalizeNodeVersion(nodeVersion));
                tempRoot = Path.Combine(Path.GetTempPath(), "ariadnets-node-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRoot);
                archivePath = Path.Combine(tempRoot, download.ArchiveFileName);
                extractRoot = Path.Combine(tempRoot, "extract");
                Directory.CreateDirectory(extractRoot);

                EditorUtility.DisplayProgressBar("AriadneTS Node.js Toolchain", "Downloading " + download.Url, 0.1f);
                using (var client = new WebClient())
                {
                    client.DownloadFile(download.Url, archivePath);
                }

                EditorUtility.DisplayProgressBar("AriadneTS Node.js Toolchain", "Extracting archive", 0.6f);
                ExtractNodeArchive(archivePath, extractRoot, download.IsZip);

                var extractedDirectory = FindSingleExtractedDirectory(extractRoot);
                if (string.IsNullOrWhiteSpace(extractedDirectory))
                {
                    throw new InvalidOperationException("Could not find the extracted Node.js directory.");
                }

                if (Directory.Exists(ProjectToolchainNodeRoot))
                {
                    Directory.Delete(ProjectToolchainNodeRoot, true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(ProjectToolchainNodeRoot) ?? ProjectRoot);
                Directory.Move(extractedDirectory, ProjectToolchainNodeRoot);

                nodeExecutable = DefaultProjectNodeExecutable;
                npmExecutable = DefaultProjectNpmExecutable;
                buildLog = "Installed AriadneTS project Node.js toolchain:\n" +
                    ProjectToolchainNodeRoot +
                    "\n\nNode: " +
                    ReadNodeVersionText() +
                    "\nnpm: " +
                    NpmStatusText();
                AssetDatabase.Refresh();
                Repaint();
            }
            catch (Exception exception)
            {
                buildLog = exception.ToString();
                EditorUtility.DisplayDialog("AriadneTS Node.js Toolchain Install Failed", exception.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                CleanupTemporaryPath(archivePath);
                CleanupTemporaryPath(extractRoot);
                CleanupTemporaryPath(tempRoot);
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
            InitializeVsCodeDebugConfig();
            AssetDatabase.Refresh();
            buildLog = "Initialized TypeScript project at:\n" + TypeScriptRoot +
                "\n\nVSCode debug config:\n" + VsCodeLaunchJsonPath;
            Repaint();
        }

        private void InitializeVsCodeDebugConfig()
        {
            UpsertVsCodeDebugConfig("127.0.0.1", 9229);
            buildLog = "Created VSCode debug config at:\n" + VsCodeLaunchJsonPath;
            Repaint();
        }

        private static bool IsVsCodeDebugConfigInitialized()
        {
            if (!File.Exists(VsCodeLaunchJsonPath))
            {
                return false;
            }

            var contents = File.ReadAllText(VsCodeLaunchJsonPath);
            return contents.Contains("\"type\": \"ariadnets\"");
        }

        private void DiagnoseTypeScriptEnvironment()
        {
            var builder = new StringBuilder();
            builder.AppendLine("AriadneTS TypeScript Environment");
            builder.AppendLine("Unity Project Root: " + ProjectRoot);
            builder.AppendLine("TypeScript Root: " + TypeScriptRoot);
            builder.AppendLine("Target Node Version: " + NormalizeNodeVersion(nodeVersion));
            builder.AppendLine("TypeScript Project: " + (IsTypeScriptProjectInitialized() ? "Ready" : "Missing or incomplete"));
            builder.AppendLine("Node Executable: " + (string.IsNullOrWhiteSpace(nodeExecutable) ? "Not selected" : nodeExecutable));
            if (File.Exists(nodeExecutable))
            {
                builder.AppendLine("Node Version: " + ReadNodeVersionText());
            }
            else
            {
                builder.AppendLine("Node Version: Missing");
            }

            var npmCliScript = ResolveNpmCliScript();
            if (!string.IsNullOrWhiteSpace(npmCliScript))
            {
                builder.AppendLine("npm: " + npmCliScript);
                builder.AppendLine("npm Version: " + ReadNpmPackageVersion(npmCliScript));
                AppendCompatibilityResult(builder, () => ValidateNpmCompatibility(npmCliScript));
            }
            else
            {
                builder.AppendLine("npm: " + (string.IsNullOrWhiteSpace(npmExecutable) ? "Not found" : npmExecutable));
                if (!string.IsNullOrWhiteSpace(npmExecutable))
                {
                    var nodeDirectory = Path.GetDirectoryName(nodeExecutable);
                    var npmVersionResult = RunProcess(npmExecutable, "--version", ProjectRoot, nodeDirectory);
                    builder.AppendLine("npm Version Output: " + npmVersionResult.CombinedOutput.Trim());
                    AppendCompatibilityResult(builder, () => ValidateNpmExecutableCompatibility(npmExecutable, nodeDirectory));
                }
            }

            builder.AppendLine("Local TypeScript Compiler: " + TypeScriptDependenciesStatusText());
            builder.AppendLine("VSCode Debug Config: " + (IsVsCodeDebugConfigInitialized() ? VsCodeLaunchJsonPath : "Missing"));
            buildLog = builder.ToString();
            Repaint();
        }

        private static void AppendCompatibilityResult(StringBuilder builder, Action validator)
        {
            try
            {
                validator();
                builder.AppendLine("Node/npm Compatibility: Ready");
            }
            catch (Exception exception)
            {
                builder.AppendLine("Node/npm Compatibility: Incompatible");
                builder.AppendLine(exception.Message);
            }
        }

        private void InstallTypeScriptDependencies()
        {
            try
            {
                var npmCliScript = ResolveNpmCliScript();
                ProcessResult result;
                if (!string.IsNullOrWhiteSpace(npmCliScript) && File.Exists(nodeExecutable))
                {
                    ValidateNpmCompatibility(npmCliScript);
                    result = RunProcess(nodeExecutable, Quote(npmCliScript) + " install", TypeScriptRoot);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(npmExecutable) || !File.Exists(npmExecutable))
                    {
                        throw new InvalidOperationException(
                            "Could not locate the project npm executable. Install/Change Project Node.js Toolchain first.");
                    }

                    var nodeDirectory = Path.GetDirectoryName(nodeExecutable);
                    ValidateNpmExecutableCompatibility(npmExecutable, nodeDirectory);
                    result = RunProcess(
                        npmExecutable,
                        "install",
                        TypeScriptRoot,
                        nodeDirectory);
                }

                buildLog = result.CombinedOutput;
                if (result.ExitCode != 0)
                {
                    EditorUtility.DisplayDialog("AriadneTS Local TypeScript Compiler Install Failed", buildLog, "OK");
                    return;
                }

                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog(
                    "AriadneTS Local TypeScript Compiler Installed",
                    "The local TypeScript compiler was installed for the generated TypeScript workspace.",
                    "OK");
            }
            catch (Exception exception)
            {
                buildLog = exception.ToString();
                EditorUtility.DisplayDialog("AriadneTS Local TypeScript Compiler Install Failed", exception.Message, "OK");
            }
        }

        private void InstallVsCodeDebuggerExtension()
        {
            var script = Path.Combine(GetPackageRoot(), "Editor", "Tools", "install_vscode_debugger_extension.mjs");
            var arguments = Quote(script);
            try
            {
                var result = RunProcess(nodeExecutable, arguments, ProjectRoot);
                buildLog = result.CombinedOutput;
                if (result.ExitCode != 0)
                {
                    EditorUtility.DisplayDialog("AriadneTS VSCode Install Failed", buildLog, "OK");
                    return;
                }

                InitializeVsCodeDebugConfig();
                buildLog += "\nVSCode launch config:\n" + VsCodeLaunchJsonPath;
                EditorUtility.DisplayDialog(
                    "AriadneTS VSCode Debugger Installed",
                    "The VSCode extension was installed and launch.json was created or verified. Restart VSCode or run Developer: Reload Window, then open the Unity project root.",
                    "OK");
            }
            catch (Exception exception)
            {
                buildLog = exception.ToString();
                EditorUtility.DisplayDialog("AriadneTS VSCode Install Failed", exception.Message, "OK");
            }
        }

        private void SyncDebugConfiguration()
        {
            ApplyDebugSettingsToSceneRuntimeHosts();
            WriteOrUpdateVsCodeDebugConfig();
        }

        private void ApplyDebugSettingsToSceneRuntimeHosts()
        {
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

                Undo.RecordObject(runtimeHost, "Apply AriadneTS Debug Settings");
                ApplyDebugSettings(runtimeHost);
                EditorUtility.SetDirty(runtimeHost);
                EditorSceneManager.MarkSceneDirty(runtimeHost.gameObject.scene);
                ++updated;
            }

            buildLog = "Applied AriadneTS debug settings to " +
                updated.ToString(CultureInfo.InvariantCulture) +
                " runtime host(s). Port: " +
                Mathf.Clamp(debugBasePort + debugInstanceId, 1, 65535).ToString(CultureInfo.InvariantCulture);
        }

        private void WriteOrUpdateVsCodeDebugConfig()
        {
            UpsertVsCodeDebugConfig(debugHost, Mathf.Clamp(debugBasePort + debugInstanceId, 1, 65535));
        }

        private void UpsertVsCodeDebugConfig(string host, int port)
        {
            var script = Path.Combine(GetPackageRoot(), "Editor", "Tools", "upsert_vscode_launch_config.mjs");
            var arguments =
                Quote(script) +
                " --launch-json " + Quote(VsCodeLaunchJsonPath) +
                " --host " + Quote(string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host) +
                " --port " + Mathf.Clamp(port, 1, 65535).ToString(CultureInfo.InvariantCulture) +
                " --ts-root " + Quote("${workspaceFolder}/TypeScript") +
                " --poll-interval-ms 250";
            var result = RunProcess(nodeExecutable, arguments, ProjectRoot);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.CombinedOutput);
            }
        }

        private void ApplyDebugSettings(ScriptRuntimeHost runtimeHost)
        {
            SetBool(runtimeHost, "enableScriptDebugging", debugEnabled);
            SetEnum(runtimeHost, "debugProtocol", (int)debugProtocol);
            SetString(runtimeHost, "debugHost", debugHost);
            SetInt(runtimeHost, "debugBasePort", debugBasePort);
            SetInt(runtimeHost, "debugInstanceId", debugInstanceId);
            SetString(runtimeHost, "debugRole", debugRole);
            SetBool(runtimeHost, "waitForDebugger", debugWaitForAttach);
            SetInt(runtimeHost, "debugStartupGraceMilliseconds", debugStartupGraceMilliseconds);
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

        private static bool AreTypeScriptDependenciesInstalled()
        {
            return File.Exists(Path.Combine(TypeScriptRoot, "node_modules", "typescript", "package.json")) &&
                (File.Exists(Path.Combine(TypeScriptRoot, "node_modules", "typescript", "bin", "tsc")) ||
                 File.Exists(Path.Combine(TypeScriptRoot, "node_modules", "typescript", "lib", "tsc.js")));
        }

        private static string TypeScriptDependenciesStatusText()
        {
            if (!File.Exists(Path.Combine(TypeScriptRoot, "package.json")))
            {
                return "Initialize the TypeScript project first.";
            }

            return AreTypeScriptDependenciesInstalled()
                ? Path.Combine(TypeScriptRoot, "node_modules", "typescript")
                : "Missing local TypeScript compiler. Use Install Local TypeScript Compiler or provide a compatible global TypeScript 5.x compiler.";
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
                " --required-abi 5";

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
                var importedPackage = ImportAndValidateBuiltPackage();
                UpdateRuntimeHostPackageReference(importedPackage);
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
            ApplyDebugSettings(runtimeHost);
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

        private TextAsset ImportAndValidateBuiltPackage()
        {
            var packageAsset = LoadTextAssetFromAbsolutePath(outputPackagePath, true);
            if (packageAsset == null)
            {
                throw new InvalidOperationException(
                    "The built package must be located under the Unity project's Assets folder.");
            }

            var packageReader = new ScriptPackageReader(
                publicKey,
                UnityManifestSerializer.Deserialize);
            var package = packageReader.Read(packageAsset.bytes);
            if (!string.Equals(package.Manifest.Version, version, StringComparison.Ordinal) ||
                package.Manifest.BuildNumber != buildNumber)
            {
                throw new InvalidOperationException(
                    "Unity imported a stale script package. Expected " +
                    version +
                    " build " +
                    buildNumber.ToString(CultureInfo.InvariantCulture) +
                    ", but imported " +
                    package.Manifest.Version +
                    " build " +
                    package.Manifest.BuildNumber.ToString(CultureInfo.InvariantCulture) +
                    ".");
            }

            buildLog = (buildLog ?? string.Empty).TrimEnd() +
                "\nImported Unity package: " +
                package.Manifest.Version +
                " build " +
                package.Manifest.BuildNumber.ToString(CultureInfo.InvariantCulture);
            return packageAsset;
        }

        private static void UpdateRuntimeHostPackageReference(TextAsset packageAsset)
        {
            var hostObject = GameObject.Find(RuntimeHostObjectName);
            if (hostObject == null)
            {
                return;
            }

            var bootstrapper = hostObject.GetComponent<ScriptPackageBootstrapper>();
            if (bootstrapper == null)
            {
                return;
            }

            SetObjectReference(bootstrapper, "packageAsset", packageAsset);
            SetBool(bootstrapper, "startOnStart", packageAsset != null);
            EditorUtility.SetDirty(bootstrapper);
            EditorSceneManager.MarkSceneDirty(hostObject.scene);
        }

        private static ProcessResult RunProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            string extraPathDirectory = null)
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
            if (!string.IsNullOrWhiteSpace(extraPathDirectory) && Directory.Exists(extraPathDirectory))
            {
                var currentPath = startInfo.Environment.ContainsKey("PATH")
                    ? startInfo.Environment["PATH"]
                    : Environment.GetEnvironmentVariable("PATH");
                startInfo.Environment["PATH"] = string.IsNullOrWhiteSpace(currentPath)
                    ? extraPathDirectory
                    : extraPathDirectory + Path.PathSeparator + currentPath;
            }

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

        private static TextAsset LoadTextAssetFromAbsolutePath(
            string absolutePath,
            bool forceSynchronousImport = false)
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

            var importOptions = forceSynchronousImport
                ? ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
                : ImportAssetOptions.Default;
            AssetDatabase.ImportAsset(relativePath, importOptions);
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

        private static void SetInt(UnityEngine.Object target, string propertyName, int value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.intValue = value;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static void SetEnum(UnityEngine.Object target, string propertyName, int value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.enumValueIndex = value;
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

        private static NodeDownloadInfo CreateNodeDownloadInfo(string version)
        {
            var platform = Application.platform == RuntimePlatform.WindowsEditor
                ? "win"
                : "darwin";
            var arch = IsArm64Process() ? "arm64" : "x64";
            var extension = platform == "win" ? "zip" : "tar.gz";
            var normalizedVersion = NormalizeNodeVersion(version);
            var packageName = "node-v" + normalizedVersion + "-" + platform + "-" + arch;
            var archiveFileName = packageName + "." + extension;
            return new NodeDownloadInfo(
                "https://nodejs.org/dist/v" + normalizedVersion + "/" + archiveFileName,
                archiveFileName,
                extension == "zip");
        }

        private static string NormalizeNodeVersion(string version)
        {
            var normalized = (version ?? string.Empty).Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1);
            }

            return string.IsNullOrWhiteSpace(normalized)
                ? DefaultNodeVersion
                : normalized;
        }

        private static bool IsArm64Process()
        {
            var processor = (SystemInfo.processorType ?? string.Empty).ToLowerInvariant();
            return processor.Contains("arm") || processor.Contains("apple");
        }

        private static void ExtractNodeArchive(string archivePath, string extractRoot, bool isZip)
        {
            if (isZip)
            {
                var powershell = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe");
                var zipResult = RunProcess(
                    powershell,
                    "-NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath " +
                    QuoteForPowerShell(archivePath) +
                    " -DestinationPath " +
                    QuoteForPowerShell(extractRoot) +
                    " -Force\"",
                    ProjectRoot);
                if (zipResult.ExitCode != 0)
                {
                    throw new InvalidOperationException(zipResult.CombinedOutput);
                }
                return;
            }

            var tarResult = RunProcess("/usr/bin/tar", "-xzf " + Quote(archivePath) + " -C " + Quote(extractRoot), ProjectRoot);
            if (tarResult.ExitCode != 0)
            {
                throw new InvalidOperationException(tarResult.CombinedOutput);
            }
        }

        private static string QuoteForPowerShell(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static string FindSingleExtractedDirectory(string extractRoot)
        {
            var directories = Directory.GetDirectories(extractRoot);
            return directories.Length == 1 ? directories[0] : string.Empty;
        }

        private static void CleanupTemporaryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Temporary cleanup should not hide the original install result.
            }
        }

        private bool HasUsableNpm()
        {
            return !string.IsNullOrWhiteSpace(ResolveNpmCliScript()) ||
                (!string.IsNullOrWhiteSpace(npmExecutable) && File.Exists(npmExecutable));
        }

        private string NpmStatusText()
        {
            var npmCliScript = ResolveNpmCliScript();
            if (!string.IsNullOrWhiteSpace(npmCliScript))
            {
                return npmCliScript + " (" + ReadNpmPackageVersion(npmCliScript) + ")";
            }

            return string.IsNullOrWhiteSpace(npmExecutable)
                ? "Not selected"
                : npmExecutable;
        }

        private string ResolveNpmCliScript()
        {
            var configuredCliScript = FindNpmCliScriptFromExecutable(npmExecutable);
            if (!string.IsNullOrWhiteSpace(configuredCliScript))
            {
                return configuredCliScript;
            }

            return FindNpmCliScriptFromNode(nodeExecutable);
        }

        private static string FindNpmCliScriptFromExecutable(string selectedNpmExecutable)
        {
            if (string.IsNullOrWhiteSpace(selectedNpmExecutable))
            {
                return string.Empty;
            }

            if (selectedNpmExecutable.EndsWith("npm-cli.js", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(selectedNpmExecutable))
            {
                return selectedNpmExecutable;
            }

            var npmDirectory = Path.GetDirectoryName(selectedNpmExecutable);
            if (string.IsNullOrWhiteSpace(npmDirectory))
            {
                return string.Empty;
            }

            var candidates = new[]
            {
                Path.Combine(npmDirectory, "node_modules", "npm", "bin", "npm-cli.js"),
                Path.GetFullPath(Path.Combine(npmDirectory, "..", "lib", "node_modules", "npm", "bin", "npm-cli.js")),
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

        private static string FindNpmCliScriptFromNode(string selectedNodeExecutable)
        {
            if (string.IsNullOrWhiteSpace(selectedNodeExecutable))
            {
                return string.Empty;
            }

            var nodeDirectory = Path.GetDirectoryName(selectedNodeExecutable);
            if (string.IsNullOrWhiteSpace(nodeDirectory))
            {
                return string.Empty;
            }

            var candidates = new[]
            {
                Path.Combine(nodeDirectory, "node_modules", "npm", "bin", "npm-cli.js"),
                Path.GetFullPath(Path.Combine(nodeDirectory, "..", "lib", "node_modules", "npm", "bin", "npm-cli.js")),
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

        private void ValidateNpmCompatibility(string npmCliScript)
        {
            var nodeMajorVersion = ReadNodeMajorVersion();
            var npmVersion = ReadNpmPackageVersion(npmCliScript);
            var npmMajorVersion = ParseMajorVersion(npmVersion);
            if (nodeMajorVersion >= 20 && npmMajorVersion > 0 && npmMajorVersion < 10)
            {
                throw new InvalidOperationException(
                    "The selected Node.js installation is using an npm version that is too old for it.\n\n" +
                    "Node: " + nodeExecutable + "\n" +
                    "Node major version: " + nodeMajorVersion.ToString(CultureInfo.InvariantCulture) + "\n" +
                    "npm: " + npmCliScript + "\n" +
                    "npm version: " + npmVersion + "\n\n" +
                    "Recommended fixes:\n" +
                    "1. Install a Node.js LTS or current release that bundles a modern npm.\n" +
                    "2. In this window, set Node Executable to that installation's node binary.\n" +
                    "3. Or update npm for the selected Node installation, then retry Install Local TypeScript Compiler.");
            }
        }

        private void ValidateNpmExecutableCompatibility(string npmExecutable, string nodeDirectory)
        {
            var nodeMajorVersion = ReadNodeMajorVersion();
            var result = RunProcess(npmExecutable, "--version", ProjectRoot, nodeDirectory);
            var npmMajorVersion = ParseMajorVersion(result.Stdout.Trim());
            if (nodeMajorVersion >= 20 &&
                (npmMajorVersion > 0 && npmMajorVersion < 10 ||
                 result.CombinedOutput.IndexOf("does not support Node.js", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                throw new InvalidOperationException(
                    "The selected Node.js installation is using an npm version that is too old for it.\n\n" +
                    "Node: " + nodeExecutable + "\n" +
                    "Node major version: " + nodeMajorVersion.ToString(CultureInfo.InvariantCulture) + "\n" +
                    "npm: " + npmExecutable + "\n" +
                    "npm version output: " + result.CombinedOutput.Trim() + "\n\n" +
                    "Recommended fixes:\n" +
                    "1. Install a Node.js LTS or current release that bundles a modern npm.\n" +
                    "2. In this window, set Node Executable to that installation's node binary.\n" +
                    "3. Or update npm for the selected Node installation, then retry Install Local TypeScript Compiler.");
            }
        }

        private string ReadNodeVersionText()
        {
            if (string.IsNullOrWhiteSpace(nodeExecutable) || !File.Exists(nodeExecutable))
            {
                return string.Empty;
            }

            var result = RunProcess(nodeExecutable, "--version", ProjectRoot);
            return result.ExitCode == 0 ? result.CombinedOutput.Trim() : result.CombinedOutput.Trim();
        }

        private int ReadNodeMajorVersion()
        {
            if (string.IsNullOrWhiteSpace(nodeExecutable) || !File.Exists(nodeExecutable))
            {
                return 0;
            }

            var result = RunProcess(nodeExecutable, "--version", ProjectRoot);
            if (result.ExitCode != 0)
            {
                return 0;
            }

            var match = Regex.Match(result.CombinedOutput, @"v?(\d+)\.");
            return match.Success
                ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : 0;
        }

        private static string ReadNpmPackageVersion(string npmCliScript)
        {
            if (string.IsNullOrWhiteSpace(npmCliScript))
            {
                return string.Empty;
            }

            var packageJson = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(npmCliScript) ?? string.Empty,
                "..",
                "package.json"));
            if (!File.Exists(packageJson))
            {
                return string.Empty;
            }

            var contents = File.ReadAllText(packageJson);
            var match = Regex.Match(contents, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static int ParseMajorVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return 0;
            }

            var match = Regex.Match(version, @"^(\d+)\.");
            return match.Success
                ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : 0;
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

        private readonly struct NodeDownloadInfo
        {
            public NodeDownloadInfo(string url, string archiveFileName, bool isZip)
            {
                Url = url;
                ArchiveFileName = archiveFileName;
                IsZip = isZip;
            }

            public string Url { get; }
            public string ArchiveFileName { get; }
            public bool IsZip { get; }
        }

        private readonly struct NodeVersionInfo
        {
            public NodeVersionInfo(string version, string date, string npmVersion, string ltsName)
            {
                Version = version;
                Date = date;
                NpmVersion = npmVersion;
                LtsName = ltsName;
            }

            public string Version { get; }
            public string Date { get; }
            public string NpmVersion { get; }
            public string LtsName { get; }
            public bool IsLts => !string.IsNullOrWhiteSpace(LtsName);
            public string DisplayLabel =>
                "v" +
                Version +
                (IsLts ? " LTS (" + LtsName + ")" : string.Empty) +
                (string.IsNullOrWhiteSpace(NpmVersion) ? string.Empty : " npm " + NpmVersion) +
                (string.IsNullOrWhiteSpace(Date) ? string.Empty : " " + Date);
        }
    }
}
