using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace StrangeApe.OpenUnityMcp
{
    internal static class OpenUnityMcpSkillSetup
    {
        private static readonly string[] Files = { "SKILL.md", "references/sdk.md" };

        internal static string Install(string projectRoot, string client, string sourceRoot)
        {
            if (client != "codex" && client != "claude-code") throw new ArgumentException("Unsupported skill client: " + client);
            var root = Path.GetFullPath(projectRoot);
            var destination = Path.Combine(root, client == "codex" ? ".agents" : ".claude", "skills", "open-unity-mcp");
            var manifest = Path.Combine(destination, ".open-unity-mcp.json");
            CheckPath(root, manifest);
            var previous = File.Exists(manifest) ? McpJson.Parse(File.ReadAllText(manifest)) as Dictionary<string, object> : null;
            var contents = new Dictionary<string, string>();
            var hashes = new Dictionary<string, object>();
            // Check every managed file before changing any. Unmanaged files are untouched.
            foreach (var relative in Files)
            {
                var target = Path.Combine(destination, relative);
                CheckPath(root, target);
                var content = File.ReadAllText(Path.Combine(sourceRoot, relative), Encoding.UTF8);
                contents[relative] = content;
                hashes[relative] = Hash(content);
                if (!File.Exists(target)) continue;
                var current = File.ReadAllText(target, Encoding.UTF8);
                if (Hash(current) == Hash(content)) continue;
                if (previous == null || !previous.TryGetValue(relative, out var expected) || Hash(current) != expected as string)
                    throw new InvalidOperationException("Preserved customized skill file: " + target + ". Move your custom skill or merge the bundled version manually.");
            }
            foreach (var item in contents)
            {
                var target = Path.Combine(destination, item.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllText(target, item.Value, new UTF8Encoding(false));
            }
            File.WriteAllText(manifest, McpJson.Stringify(hashes), new UTF8Encoding(false));
            return Path.Combine(destination, "SKILL.md");
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))));
        }

        private static void CheckPath(string root, string target)
        {
            for (var path = target; !string.Equals(path, root, StringComparison.OrdinalIgnoreCase); path = Path.GetDirectoryName(path))
            {
                if (string.IsNullOrEmpty(path) || !Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Skill destination must remain inside the project.");
                if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Refusing to overwrite a linked skill path: " + path);
            }
        }
    }
}
