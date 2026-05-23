using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class OpenUnityMcpClientSetup
    {
        private const string ServerName = "open-unity-mcp";
        private const string CodexSectionName = "mcp_servers." + ServerName;
        private const string ClaudeDesktopBridgePackage = "mcp-remote@latest";
        private const string NewLine = "\r\n";

        [MenuItem("Tools/Open Unity MCP/Setup/Claude Code Project")]
        public static void InstallClaudeCodeProjectConfigMenu()
        {
            InstallWithDialog(
                "Claude Code",
                () => InstallClaudeCodeProjectConfig(ProjectRoot, OpenUnityMcpSettings.Endpoint),
                "Restart Claude Code, or run /mcp in an active session to check the connection.");
        }

        [MenuItem("Tools/Open Unity MCP/Setup/Codex User Config")]
        public static void InstallCodexConfigMenu()
        {
            InstallWithDialog(
                "Codex",
                () => InstallCodexConfig(CodexConfigPath, OpenUnityMcpSettings.Endpoint),
                "Restart Codex, then run codex mcp list to check the connection.");
        }

        [MenuItem("Tools/Open Unity MCP/Setup/Claude Desktop Bridge")]
        public static void InstallClaudeDesktopConfigMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Setup Claude Desktop",
                    "Claude Desktop starts local MCP servers as processes. This will add a local stdio bridge that runs npx -y mcp-remote@latest --http " + OpenUnityMcpSettings.Endpoint + " --allow-http.\n\nNode.js/npm must be installed for the bridge to run.",
                    "Update Config",
                    "Cancel"))
            {
                return;
            }

            InstallWithDialog(
                "Claude Desktop",
                () => string.Join("\n", InstallClaudeDesktopConfigs(OpenUnityMcpSettings.Endpoint).ToArray()),
                "Restart Claude Desktop, or reload MCP configuration from Settings > Developer.");
        }

        public static void DrawClientSetupGui()
        {
            EditorGUILayout.LabelField("Client Setup", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Claude Code", ProjectRelativeClaudeCodeConfigPath);
            EditorGUILayout.LabelField("Codex", CodexConfigPath);
            var claudeDesktopPaths = ClaudeDesktopConfigPaths;
            for (var i = 0; i < claudeDesktopPaths.Count; i++)
            {
                EditorGUILayout.LabelField(i == 0 ? "Claude Desktop" : "Claude Desktop Alt", claudeDesktopPaths[i]);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Setup Claude Code"))
                {
                    InstallClaudeCodeProjectConfigMenu();
                }

                if (GUILayout.Button("Setup Codex"))
                {
                    InstallCodexConfigMenu();
                }
            }

            if (GUILayout.Button("Setup Claude Desktop Bridge"))
            {
                InstallClaudeDesktopConfigMenu();
            }

            EditorGUILayout.HelpBox("Claude Desktop uses a stdio bridge because its local MCP config starts processes. Codex and Claude Code connect to the HTTP endpoint directly.", MessageType.Info);
        }

        internal static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        internal static string ProjectRelativeClaudeCodeConfigPath => Path.Combine(ProjectRoot, ".mcp.json");

        internal static string CodexConfigPath => Path.Combine(GetHomeDirectory(), ".codex", "config.toml");

        internal static string ClaudeDesktopConfigPath => ClaudeDesktopConfigPaths[0];

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

        internal static string InstallClaudeCodeProjectConfig(string projectRoot, string endpoint)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            var configPath = Path.Combine(projectRoot, ".mcp.json");
            UpsertJsonServerConfig(configPath, CreateHttpServerConfig(endpoint));
            return configPath;
        }

        internal static string InstallClaudeDesktopConfig(string configPath, string endpoint)
        {
            UpsertJsonServerConfig(configPath, CreateClaudeDesktopBridgeConfig(endpoint));
            return configPath;
        }

        internal static List<string> InstallClaudeDesktopConfigs(string endpoint)
        {
            var updatedPaths = new List<string>();
            foreach (var configPath in ClaudeDesktopConfigPaths)
            {
                InstallClaudeDesktopConfig(configPath, endpoint);
                updatedPaths.Add(configPath);
            }

            return updatedPaths;
        }

        internal static string InstallCodexConfig(string configPath, string endpoint)
        {
            if (string.IsNullOrEmpty(configPath))
            {
                throw new ArgumentException("Config path is required.", nameof(configPath));
            }

            var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
            WriteTextIfChanged(configPath, UpsertCodexConfigText(existing, endpoint));
            return configPath;
        }

        internal static string UpsertCodexConfigText(string existing, string endpoint)
        {
            RequireEndpoint(endpoint);

            var lines = SplitContentLines(existing);
            RemoveTomlSection(lines, CodexSectionName);

            if (lines.Count > 0 && lines[lines.Count - 1].Length != 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add("[" + CodexSectionName + "]");
            lines.Add("url = " + McpJson.Stringify(endpoint));

            return JoinLines(lines);
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

        private static Dictionary<string, object> CreateClaudeDesktopBridgeConfig(string endpoint)
        {
            RequireEndpoint(endpoint);

            return McpJson.Object(
                "command", "npx",
                "args", McpJson.Array(
                    "-y",
                    ClaudeDesktopBridgePackage,
                    "--http",
                    endpoint,
                    "--allow-http"));
        }

        private static void UpsertJsonServerConfig(string configPath, Dictionary<string, object> serverConfig)
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

            servers[ServerName] = serverConfig;
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

        private static string GetHomeDirectory()
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
