using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class OpenUnityMcpClientSetup
    {
        private const string ServerName = "open-unity-mcp";
        private const string HttpFallbackServerName = "open-unity-mcp-http";
        private const string CodexSectionName = "mcp_servers." + ServerName;
        private const string NewLine = "\r\n";

        [MenuItem("Tools/Open Unity MCP/Setup/Claude Code Project", false, 60)]
        public static void InstallClaudeCodeProjectConfigMenu()
        {
            InstallWithDialog(
                "Claude Code",
                () => InstallSkillAlongsideConfig(InstallClaudeCodeProjectConfig(ProjectRoot, ResolveLaunch()), "claude-code"),
                "Restart Claude Code, or run /mcp in an active session to check the connection.\n\nThe sidecar rides out Unity domain reloads so the connection survives recompiles." +
                HttpFallbackTokenNote());
        }

        // When token enforcement is on, the named HTTP fallback entry cannot work
        // without a manually-added Authorization header — we never write the secret
        // into .mcp.json (it would be committable). The sidecar entry is unaffected
        // because it reads the token from the gitignored status file. This note is
        // appended to setup dialogs so the user knows; it does NOT contain the token.
        private static string HttpFallbackTokenNote()
        {
            if (!OpenUnityMcpSettings.RequireAccessToken)
            {
                return string.Empty;
            }

            return "\n\nAccess token enforcement is ON. The stdio sidecar entry (open-unity-mcp) sends the " +
                   "token automatically. The named HTTP fallback entry (open-unity-mcp-http) will be REJECTED " +
                   "unless you add an 'Authorization: Bearer <token>' header yourself — the token is deliberately " +
                   "not written into .mcp.json. Copy it from Preferences > Open Unity MCP if you need the fallback.";
        }

        [MenuItem("Tools/Open Unity MCP/Setup/Codex User Config", false, 61)]
        public static void InstallCodexConfigMenu()
        {
            InstallWithDialog(
                "Codex",
                () => InstallSkillAlongsideConfig(InstallCodexConfig(CodexConfigPath, ResolveLaunch()), "codex"),
                "Restart Codex, then run codex mcp list to check the connection. The Unity workflow skill is installed in this project's .agents/skills folder.");
        }

        [MenuItem("Tools/Open Unity MCP/Setup/Claude Desktop Bridge", false, 62)]
        public static void InstallClaudeDesktopConfigMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Setup Claude Desktop",
                    "Claude Desktop starts local MCP servers as processes. This will add the Open Unity MCP sidecar (node " + OpenUnityMcpSidecarInstaller.InstalledScriptPath.Replace('\\', '/') + "), which forwards to the in-editor server and survives domain reloads. The sidecar is kept at that fixed path so package updates never break the config.\n\nNode.js 18+ must be installed to run the sidecar.",
                    "Update Config",
                    "Cancel"))
            {
                return;
            }

            InstallWithDialog(
                "Claude Desktop",
                () => string.Join("\n", InstallClaudeDesktopConfigs(ResolveLaunch()).ToArray()),
                "Restart Claude Desktop, or reload MCP configuration from Settings > Developer.");
        }

        public static void DrawClientSetupGui()
        {
            EditorGUILayout.LabelField("Client Setup", EditorStyles.boldLabel);

            DrawClientBlock(
                "Claude Code",
                "stdio session + project skill in .claude/skills",
                new[] { ProjectRelativeClaudeCodeConfigPath },
                "Setup Claude Code",
                InstallClaudeCodeProjectConfigMenu);

            DrawClientBlock(
                "Codex",
                "stdio session + project skill in .agents/skills",
                new[] { CodexConfigPath },
                "Setup Codex",
                InstallCodexConfigMenu);

            var claudeDesktopPaths = ClaudeDesktopConfigPaths;
            DrawClientBlock(
                "Claude Desktop",
                "stdio sidecar via node (requires Node.js 18+)",
                claudeDesktopPaths.ToArray(),
                "Setup Claude Desktop Bridge",
                InstallClaudeDesktopConfigMenu);

            EditorGUILayout.Space();
            DrawCustomClientBlock();
        }

        private static void DrawClientBlock(string title, string subtitle, string[] paths, string buttonLabel, Action onClick)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(subtitle))
                {
                    EditorGUILayout.LabelField(subtitle, EditorStyles.miniLabel);
                }

                for (var i = 0; i < paths.Length; i++)
                {
                    EditorGUILayout.SelectableLabel(paths[i], EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }

                if (GUILayout.Button(buttonLabel))
                {
                    onClick();
                }
            }
        }

        private static void DrawCustomClientBlock()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Custom MCP Client", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("For clients that launch a process, use the sidecar command below (it survives domain reloads). Clients that speak Streamable HTTP directly can use the URL, but will drop on every recompile.", EditorStyles.wordWrappedMiniLabel);

                var launch = TryResolveLaunch();
                if (launch.HasValue)
                {
                    DrawCopyableRow("Sidecar", launch.Value.CommandLine);
                }
                else
                {
                    EditorGUILayout.LabelField("Sidecar not installed yet (" + OpenUnityMcpSidecarInstaller.InstallDirectory + ").", EditorStyles.miniLabel);
                }

                DrawCopyableRow("HTTP URL", OpenUnityMcpSettings.Endpoint);

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(ClientSetupDocPath)))
                {
                    if (GUILayout.Button("Open client-setup.md"))
                    {
                        OpenClientSetupDoc();
                    }
                }
            }
        }

        private static void DrawCopyableRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(90f));
                EditorGUILayout.SelectableLabel(value, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Copy", GUILayout.Width(60f)))
                {
                    EditorGUIUtility.systemCopyBuffer = value;
                }
            }
        }

        private static void OpenClientSetupDoc()
        {
            var path = ClientSetupDocPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                EditorUtility.DisplayDialog(
                    "Open Unity MCP",
                    "Could not locate client-setup.md in the package's Documentation~ folder.",
                    "OK");
                return;
            }

            Application.OpenURL(new Uri(path).AbsoluteUri);
        }

        private static string ClientSetupDocPath
        {
            get
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OpenUnityMcpClientSetup).Assembly);
                if (package == null || string.IsNullOrEmpty(package.resolvedPath))
                {
                    return null;
                }

                return Path.Combine(package.resolvedPath, "Documentation~", "client-setup.md");
            }
        }

        // Absolute path to the sidecar script inside the resolved package. Client
        // configs must not reference it: for git and registry packages it lives under
        // Library/PackageCache/...@<hash> and moves on every update. Configs use the
        // stable copy from OpenUnityMcpSidecarInstaller instead.
        internal static string SidecarScriptPath
        {
            get
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OpenUnityMcpClientSetup).Assembly);
                if (package == null || string.IsNullOrEmpty(package.resolvedPath))
                {
                    return null;
                }

                return Path.GetFullPath(Path.Combine(package.resolvedPath, "Server~", "open-unity-mcp-sidecar.js"));
            }
        }

        private static SidecarLaunch ResolveLaunch()
        {
            var scriptPath = OpenUnityMcpSidecarInstaller.EnsureInstalled();
            if (string.IsNullOrEmpty(scriptPath))
            {
                throw new InvalidOperationException(
                    "Could not install the sidecar script (Server~/open-unity-mcp-sidecar.js) to " +
                    OpenUnityMcpSidecarInstaller.InstallDirectory + ". Reimport the package or use the HTTP URL directly.");
            }

            return SidecarLaunch.Create(scriptPath, OpenUnityMcpSettings.Port, ProjectRoot);
        }

        // Called from OnGUI on every repaint, so it only describes the stable copy the
        // bootstrap already installed rather than refreshing it.
        private static SidecarLaunch? TryResolveLaunch()
        {
            var scriptPath = OpenUnityMcpSidecarInstaller.InstalledScriptPath;
            if (!File.Exists(scriptPath))
            {
                return null;
            }

            return SidecarLaunch.Create(scriptPath, OpenUnityMcpSettings.Port, ProjectRoot);
        }

        // Describes how a client should launch the sidecar over stdio. Built once
        // and handed to each client writer so the command line is identical across
        // Claude Code, Codex, and Claude Desktop.
        internal readonly struct SidecarLaunch
        {
            public readonly string Command;
            public readonly string[] Args;
            public readonly string Endpoint;

            private SidecarLaunch(string command, string[] args, string endpoint)
            {
                Command = command;
                Args = args;
                Endpoint = endpoint;
            }

            public static SidecarLaunch Create(string scriptPath, int port, string projectRoot)
            {
                if (string.IsNullOrEmpty(scriptPath))
                {
                    throw new ArgumentException("Sidecar script path is required.", nameof(scriptPath));
                }

                var normalizedScript = scriptPath.Replace('\\', '/');
                var normalizedProject = (projectRoot ?? string.Empty).Replace('\\', '/');
                var args = new[]
                {
                    normalizedScript,
                    "--port", port.ToString(CultureInfo.InvariantCulture),
                    "--project", normalizedProject
                };

                return new SidecarLaunch("node", args, "http://127.0.0.1:" + port + "/mcp");
            }

            public string CommandLine
            {
                get
                {
                    var builder = new StringBuilder(Command);
                    foreach (var arg in Args)
                    {
                        builder.Append(' ');
                        builder.Append(arg.IndexOf(' ') >= 0 ? "\"" + arg + "\"" : arg);
                    }

                    return builder.ToString();
                }
            }

            public void Require()
            {
                if (string.IsNullOrEmpty(Command) || Args == null || Args.Length == 0)
                {
                    throw new InvalidOperationException("Sidecar launch is not configured.");
                }
            }
        }

        private static string FormatTomlStringArray(string[] values)
        {
            var builder = new StringBuilder();
            builder.Append('[');
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(McpJson.Stringify(values[i]));
            }

            builder.Append(']');
            return builder.ToString();
        }

        private static string InstallSkillAlongsideConfig(string configPath, string client)
        {
            try
            {
                var packageRoot = Path.GetDirectoryName(Path.GetDirectoryName(SidecarScriptPath));
                var source = Path.Combine(packageRoot, "Skills~", "open-unity-mcp");
                return configPath + "\nSkill: " + OpenUnityMcpSkillSetup.Install(ProjectRoot, client, source);
            }
            catch (Exception ex)
            {
                return configPath + "\nConfig installed. Skill was not updated: " + ex.Message;
            }
        }

        internal static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        internal static string ProjectRelativeClaudeCodeConfigPath => Path.Combine(ProjectRoot, ".mcp.json");

        internal static string CodexConfigPath => Path.Combine(GetHomeDirectory(), ".codex", "config.toml");

        internal static List<string> ClaudeDesktopConfigPaths
        {
            get
            {
                var paths = new List<string>();
#if UNITY_EDITOR_OSX
                AddUniquePath(paths, Path.Combine(GetHomeDirectory(), "Library", "Application Support", "Claude", "claude_desktop_config.json"));
#elif UNITY_EDITOR_WIN
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData))
                {
                    appData = Path.Combine(GetHomeDirectory(), "AppData", "Roaming");
                }

                AddUniquePath(paths, Path.Combine(appData, "Claude", "claude_desktop_config.json"));
                AddWindowsMsixClaudeDesktopConfigPaths(paths);
#else
                AddUniquePath(paths, Path.Combine(GetHomeDirectory(), ".config", "Claude", "claude_desktop_config.json"));
#endif

                return paths;
            }
        }

        internal static string InstallClaudeCodeProjectConfig(string projectRoot, SidecarLaunch launch)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            launch.Require();
            var configPath = Path.Combine(projectRoot, ".mcp.json");
            // The sidecar is the recommended default; keep a named HTTP entry as a
            // fallback for anyone who wants to bypass Node, and so switching back is
            // a one-line edit rather than reconstructing the URL.
            UpsertJsonServerConfigs(configPath, new[]
            {
                new KeyValuePair<string, Dictionary<string, object>>(ServerName, CreateSidecarStdioConfig(launch)),
                new KeyValuePair<string, Dictionary<string, object>>(HttpFallbackServerName, CreateHttpServerConfig(launch.Endpoint))
            });
            return configPath;
        }

        internal static string InstallClaudeDesktopConfig(string configPath, SidecarLaunch launch)
        {
            launch.Require();
            UpsertJsonServerConfig(configPath, CreateSidecarStdioConfig(launch));
            return configPath;
        }

        internal static List<string> InstallClaudeDesktopConfigs(SidecarLaunch launch)
        {
            var updatedPaths = new List<string>();
            foreach (var configPath in ClaudeDesktopConfigPaths)
            {
                InstallClaudeDesktopConfig(configPath, launch);
                updatedPaths.Add(configPath);
            }

            return updatedPaths;
        }

        internal static string InstallCodexConfig(string configPath, SidecarLaunch launch)
        {
            if (string.IsNullOrEmpty(configPath))
            {
                throw new ArgumentException("Config path is required.", nameof(configPath));
            }

            launch.Require();
            var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
            WriteTextIfChanged(configPath, UpsertCodexConfigText(existing, launch));
            return configPath;
        }

        internal static string UpsertCodexConfigText(string existing, SidecarLaunch launch)
        {
            launch.Require();

            var lines = SplitContentLines(existing);
            RemoveTomlSection(lines, CodexSectionName);

            if (lines.Count > 0 && lines[lines.Count - 1].Length != 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add("[" + CodexSectionName + "]");
            lines.Add("command = " + McpJson.Stringify(launch.Command));
            lines.Add("args = " + FormatTomlStringArray(launch.Args));

            return JoinLines(lines);
        }

        // Repoints existing open-unity-mcp sidecar entries at the stable script when they
        // reference a package-resolved copy (Library/PackageCache/...@<hash>, which moves
        // on every package update) or a script that no longer exists. Runs on editor load
        // so configs written by older package versions heal themselves. Entries using the
        // HTTP url, another command, or an existing script outside PackageCache are left
        // alone, and a file that does not parse is never rewritten.
        internal static List<string> RepairSidecarConfigs(string stableScriptPath, string projectRoot)
        {
            var repaired = new List<string>();
            foreach (var desktopConfigPath in ClaudeDesktopConfigPaths)
            {
                TryRepair(repaired, desktopConfigPath, () => RepairJsonSidecarConfig(desktopConfigPath, stableScriptPath, projectRoot, false));
            }

            var projectConfigPath = Path.Combine(projectRoot, ".mcp.json");
            TryRepair(repaired, projectConfigPath, () => RepairJsonSidecarConfig(projectConfigPath, stableScriptPath, projectRoot, true));

            var codexConfigPath = CodexConfigPath;
            TryRepair(repaired, codexConfigPath, () => RepairCodexSidecarConfig(codexConfigPath, stableScriptPath, projectRoot));
            return repaired;
        }

        private static void TryRepair(List<string> repaired, string configPath, Func<bool> repair)
        {
            try
            {
                if (repair())
                {
                    repaired.Add(configPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Open Unity MCP] Could not repair " + configPath + ": " + ex.Message);
            }
        }

        // ownsProject is true for the project's own .mcp.json, whose --project must be
        // this project. Shared configs (Claude Desktop, Codex) only get --project
        // replaced when it names a directory that no longer exists.
        internal static bool RepairJsonSidecarConfig(string configPath, string stableScriptPath, string projectRoot, bool ownsProject)
        {
            if (!File.Exists(configPath))
            {
                return false;
            }

            Dictionary<string, object> root;
            try
            {
                root = ReadJsonConfig(configPath);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (!root.TryGetValue("mcpServers", out var serversValue) || !(serversValue is Dictionary<string, object> servers) ||
                !servers.TryGetValue(ServerName, out var entryValue) || !(entryValue is Dictionary<string, object> entry) ||
                !entry.TryGetValue("args", out var argsValue) || !(argsValue is List<object> args) ||
                !RepairSidecarArgs(args, stableScriptPath, projectRoot, ownsProject))
            {
                return false;
            }

            WriteTextIfChanged(configPath, SerializePrettyJson(root));
            return true;
        }

        // Rewrites only the args line of the [mcp_servers.open-unity-mcp] section so tool
        // approval subsections and the file's line endings are preserved.
        internal static bool RepairCodexSidecarConfig(string configPath, string stableScriptPath, string projectRoot)
        {
            if (!File.Exists(configPath))
            {
                return false;
            }

            var text = File.ReadAllText(configPath);
            var lines = SplitContentLines(text);
            var inSection = false;
            for (var i = 0; i < lines.Count; i++)
            {
                var section = GetTomlSectionName(lines[i]);
                if (section != null)
                {
                    inSection = string.Equals(section, CodexSectionName, StringComparison.Ordinal);
                    continue;
                }

                var trimmed = lines[i].Trim();
                var equals = trimmed.IndexOf('=');
                if (!inSection || equals < 0 || !string.Equals(trimmed.Substring(0, equals).Trim(), "args", StringComparison.Ordinal))
                {
                    continue;
                }

                List<object> args;
                try
                {
                    args = McpJson.Parse(trimmed.Substring(equals + 1).Trim()) as List<object>;
                }
                catch (FormatException)
                {
                    // TOML literal strings and multi-line arrays are not JSON; leave them alone.
                    return false;
                }

                if (args == null || !RepairSidecarArgs(args, stableScriptPath, projectRoot, false))
                {
                    return false;
                }

                var values = new string[args.Count];
                for (var j = 0; j < args.Count; j++)
                {
                    values[j] = Convert.ToString(args[j], CultureInfo.InvariantCulture);
                }

                lines[i] = "args = " + FormatTomlStringArray(values);
                var newLine = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
                WriteTextIfChanged(configPath, string.Join(newLine, lines.ToArray()) + newLine);
                return true;
            }

            return false;
        }

        // Mutates args in place and returns true when anything changed.
        internal static bool RepairSidecarArgs(IList<object> args, string stableScriptPath, string projectRoot, bool ownsProject)
        {
            var scriptIndex = -1;
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i] is string candidate &&
                    candidate.Replace('\\', '/').EndsWith("/" + OpenUnityMcpSidecarInstaller.ScriptFileName, StringComparison.OrdinalIgnoreCase))
                {
                    scriptIndex = i;
                    break;
                }
            }

            if (scriptIndex < 0)
            {
                return false;
            }

            var changed = false;
            var stable = stableScriptPath.Replace('\\', '/');
            var current = (string)args[scriptIndex];
            if (!PathsEqual(current, stable) &&
                (current.Replace('\\', '/').IndexOf("/Library/PackageCache/", StringComparison.OrdinalIgnoreCase) >= 0 || !File.Exists(current)))
            {
                args[scriptIndex] = stable;
                changed = true;
            }

            var normalizedRoot = projectRoot.Replace('\\', '/');
            for (var i = scriptIndex + 1; i + 1 < args.Count; i++)
            {
                if (!string.Equals(args[i] as string, "--project", StringComparison.Ordinal) || !(args[i + 1] is string configuredProject))
                {
                    continue;
                }

                if (ownsProject ? !PathsEqual(configuredProject, normalizedRoot) : !Directory.Exists(configuredProject))
                {
                    args[i + 1] = normalizedRoot;
                    changed = true;
                }

                break;
            }

            return changed;
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                left = Path.GetFullPath(left);
                right = Path.GetFullPath(right);
            }
            catch (Exception)
            {
                // Compare the raw strings when either path is malformed.
            }

#if UNITY_EDITOR_LINUX
            const StringComparison comparison = StringComparison.Ordinal;
#else
            const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
#endif
            return string.Equals(
                left.Replace('\\', '/').TrimEnd('/'),
                right.Replace('\\', '/').TrimEnd('/'),
                comparison);
        }

        private static void InstallWithDialog(string clientName, Func<string> install, string followUp)
        {
            try
            {
                var path = install();
                EditorUtility.DisplayDialog(
                    "Open Unity MCP",
                    clientName + " config updated:\n" + path + "\n\n" + followUp,
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "Open Unity MCP",
                    "Could not update " + clientName + " config:\n" + ex.Message,
                    "OK");
            }
        }

        private static Dictionary<string, object> CreateHttpServerConfig(string endpoint)
        {
            RequireEndpoint(endpoint);

            return McpJson.Object(
                "type", "http",
                "url", endpoint);
        }

        private static Dictionary<string, object> CreateSidecarStdioConfig(SidecarLaunch launch)
        {
            launch.Require();

            var args = new List<object>(launch.Args.Length);
            foreach (var arg in launch.Args)
            {
                args.Add(arg);
            }

            return McpJson.Object(
                "command", launch.Command,
                "args", args);
        }

        private static void UpsertJsonServerConfig(string configPath, Dictionary<string, object> serverConfig)
        {
            UpsertJsonServerConfigs(configPath, new[]
            {
                new KeyValuePair<string, Dictionary<string, object>>(ServerName, serverConfig)
            });
        }

        private static void UpsertJsonServerConfigs(string configPath, IEnumerable<KeyValuePair<string, Dictionary<string, object>>> serverConfigs)
        {
            if (string.IsNullOrEmpty(configPath))
            {
                throw new ArgumentException("Config path is required.", nameof(configPath));
            }

            var root = ReadJsonConfig(configPath);
            if (!root.TryGetValue("mcpServers", out var serversValue) || !(serversValue is Dictionary<string, object> servers))
            {
                servers = new Dictionary<string, object>(StringComparer.Ordinal);
                root["mcpServers"] = servers;
            }

            foreach (var entry in serverConfigs)
            {
                servers[entry.Key] = entry.Value;
            }

            WriteTextIfChanged(configPath, SerializePrettyJson(root));
        }

        private static Dictionary<string, object> ReadJsonConfig(string configPath)
        {
            if (!File.Exists(configPath))
            {
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }

            var text = File.ReadAllText(configPath);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }

            try
            {
                var root = McpJson.Parse(text) as Dictionary<string, object>;
                if (root == null)
                {
                    throw new FormatException("Root value must be a JSON object.");
                }

                return root;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Config is not valid JSON: " + configPath, ex);
            }
        }

        private static void RemoveTomlSection(List<string> lines, string sectionName)
        {
            for (var i = 0; i < lines.Count;)
            {
                var currentSection = GetTomlSectionName(lines[i]);
                if (currentSection != null && IsTargetTomlSection(currentSection, sectionName))
                {
                    var end = i + 1;
                    while (end < lines.Count)
                    {
                        var nextSection = GetTomlSectionName(lines[end]);
                        if (nextSection != null && !IsTargetTomlSection(nextSection, sectionName))
                        {
                            break;
                        }

                        end++;
                    }

                    lines.RemoveRange(i, end - i);
                    continue;
                }

                i++;
            }
        }

        private static bool IsTargetTomlSection(string candidate, string sectionName)
        {
            return string.Equals(candidate, sectionName, StringComparison.Ordinal)
                || candidate.StartsWith(sectionName + ".", StringComparison.Ordinal);
        }

        private static string GetTomlSectionName(string line)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return null;
            }

            var start = trimmed.StartsWith("[[", StringComparison.Ordinal) ? 2 : 1;
            var closeToken = start == 2 ? "]]" : "]";
            var close = trimmed.IndexOf(closeToken, start, StringComparison.Ordinal);
            if (close < 0)
            {
                return null;
            }

            return trimmed.Substring(start, close - start).Trim();
        }

        private static List<string> SplitContentLines(string text)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return lines;
            }

            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var parts = normalized.Split('\n');
            var length = normalized.EndsWith("\n", StringComparison.Ordinal) ? parts.Length - 1 : parts.Length;
            for (var i = 0; i < length; i++)
            {
                lines.Add(parts[i]);
            }

            return lines;
        }

        private static string JoinLines(List<string> lines)
        {
            return string.Join(NewLine, lines.ToArray()) + NewLine;
        }

        private static string SerializePrettyJson(Dictionary<string, object> root)
        {
            var builder = new StringBuilder(256);
            WritePrettyJson(builder, root, 0);
            builder.Append(NewLine);
            return builder.ToString();
        }

        private static void WritePrettyJson(StringBuilder builder, object value, int depth)
        {
            if (value is IDictionary dictionary)
            {
                builder.Append('{');
                if (dictionary.Count > 0)
                {
                    builder.Append(NewLine);
                    var first = true;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (!first)
                        {
                            builder.Append(',');
                            builder.Append(NewLine);
                        }

                        first = false;
                        WriteIndent(builder, depth + 1);
                        builder.Append(McpJson.Stringify(Convert.ToString(entry.Key)));
                        builder.Append(": ");
                        WritePrettyJson(builder, entry.Value, depth + 1);
                    }

                    builder.Append(NewLine);
                    WriteIndent(builder, depth);
                }

                builder.Append('}');
                return;
            }

            if (value is IList list)
            {
                builder.Append('[');
                if (list.Count > 0)
                {
                    builder.Append(NewLine);
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(',');
                            builder.Append(NewLine);
                        }

                        WriteIndent(builder, depth + 1);
                        WritePrettyJson(builder, list[i], depth + 1);
                    }

                    builder.Append(NewLine);
                    WriteIndent(builder, depth);
                }

                builder.Append(']');
                return;
            }

            builder.Append(McpJson.Stringify(value));
        }

        private static void WriteIndent(StringBuilder builder, int depth)
        {
            for (var i = 0; i < depth; i++)
            {
                builder.Append("  ");
            }
        }

        private static void WriteTextIfChanged(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            {
                return;
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static void AddWindowsMsixClaudeDesktopConfigPaths(List<string> paths)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                return;
            }

            var packagesPath = Path.Combine(localAppData, "Packages");
            if (!Directory.Exists(packagesPath))
            {
                return;
            }

            AddWindowsMsixClaudeDesktopConfigPaths(paths, packagesPath, "Claude_*");
            AddWindowsMsixClaudeDesktopConfigPaths(paths, packagesPath, "Anthropic.ClaudeDesktop_*");
        }

        private static void AddWindowsMsixClaudeDesktopConfigPaths(List<string> paths, string packagesPath, string pattern)
        {
            foreach (var packagePath in Directory.GetDirectories(packagesPath, pattern))
            {
                var configPath = Path.Combine(packagePath, "LocalCache", "Roaming", "Claude", "claude_desktop_config.json");
                if (File.Exists(configPath) || Directory.Exists(Path.GetDirectoryName(configPath)))
                {
                    AddUniquePath(paths, configPath);
                }
            }
        }

        private static void AddUniquePath(List<string> paths, string path)
        {
            foreach (var existing in paths)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            paths.Add(path);
        }

        internal static string GetHomeDirectory()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                return home;
            }

            home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                return home;
            }

            throw new InvalidOperationException("Could not locate the current user's home directory.");
        }

        private static void RequireEndpoint(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new ArgumentException("Endpoint is required.", nameof(endpoint));
            }
        }
    }
}
