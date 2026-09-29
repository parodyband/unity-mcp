using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    // Keeps a copy of the Node sidecar at a fixed per-user path so MCP client
    // configs never reference the package's resolved location. Git and registry
    // packages resolve to Library/PackageCache/com.strangeape.open-unity-mcp@<hash>,
    // and that hash changes on every package update: a config pointing there fails
    // with MODULE_NOT_FOUND the moment the package moves, which clients report as the
    // server disconnecting.
    //
    // The copy lives under the user's home directory rather than AppData because
    // MSIX-packaged clients (Claude Desktop from the Microsoft Store) virtualize
    // AppData for the processes they launch.
    internal static class OpenUnityMcpSidecarInstaller
    {
        internal const string ScriptFileName = "open-unity-mcp-sidecar.js";
        private const string VersionFileName = "package-version.txt";

        internal enum InstallOutcome
        {
            Installed,
            Updated,
            UpToDate,
            KeptNewer
        }

        internal static string InstallDirectory => Path.Combine(OpenUnityMcpClientSetup.GetHomeDirectory(), ".open-unity-mcp", "sidecar");

        internal static string InstalledScriptPath => Path.Combine(InstallDirectory, ScriptFileName);

        // Runs on every editor load so a package update refreshes the stable copy and
        // configs written by older versions are repointed at it.
        internal static void InstallAndRepairClients()
        {
            var scriptPath = EnsureInstalled();
            if (string.IsNullOrEmpty(scriptPath))
            {
                return;
            }

            var repaired = OpenUnityMcpClientSetup.RepairSidecarConfigs(scriptPath, OpenUnityMcpClientSetup.ProjectRoot);
            if (repaired.Count > 0)
            {
                Debug.Log("[Open Unity MCP] Repointed the open-unity-mcp sidecar entry at " + scriptPath.Replace('\\', '/') +
                          " in:\n" + string.Join("\n", repaired.ToArray()) +
                          "\nFully quit and reopen Claude Desktop, or start a new Claude Code or Codex session, to reconnect.");
            }
        }

        // Copies the package's sidecar into the stable directory and returns the stable
        // script path. Falls back to an existing stable copy if the refresh fails, and
        // returns null only when neither the package nor a previous install is usable.
        internal static string EnsureInstalled()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OpenUnityMcpSidecarInstaller).Assembly);
            if (package == null || string.IsNullOrEmpty(package.resolvedPath))
            {
                return File.Exists(InstalledScriptPath) ? InstalledScriptPath : null;
            }

            try
            {
                var outcome = Install(Path.Combine(package.resolvedPath, "Server~"), package.version, InstallDirectory);
                if (outcome == InstallOutcome.Installed || outcome == InstallOutcome.Updated)
                {
                    Debug.Log("[Open Unity MCP] Sidecar " + package.version + " " +
                              (outcome == InstallOutcome.Installed ? "installed" : "updated") + " at " + InstallDirectory + ".");
                }

                return InstalledScriptPath;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Open Unity MCP] Could not refresh the sidecar at " + InstallDirectory + ": " + ex.Message);
                return File.Exists(InstalledScriptPath) ? InstalledScriptPath : null;
            }
        }

        // Copies every top-level .js file from sourceDirectory (the sidecar requires its
        // siblings by relative path). A newer version already installed by another
        // project is kept rather than downgraded; the same version is refreshed when the
        // content differs so local package edits still propagate.
        internal static InstallOutcome Install(string sourceDirectory, string sourceVersion, string targetDirectory)
        {
            if (!File.Exists(Path.Combine(sourceDirectory, ScriptFileName)))
            {
                throw new FileNotFoundException("Sidecar script not found in " + sourceDirectory + ".");
            }

            var versionPath = Path.Combine(targetDirectory, VersionFileName);
            var installedVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : null;
            var existed = File.Exists(Path.Combine(targetDirectory, ScriptFileName));
            if (existed && CompareVersions(sourceVersion, installedVersion) < 0)
            {
                return InstallOutcome.KeptNewer;
            }

            Directory.CreateDirectory(targetDirectory);
            var changed = false;
            foreach (var sourceFile in Directory.GetFiles(sourceDirectory, "*.js", SearchOption.TopDirectoryOnly))
            {
                var bytes = File.ReadAllBytes(sourceFile);
                var targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));
                if (File.Exists(targetFile) && BytesEqual(File.ReadAllBytes(targetFile), bytes))
                {
                    continue;
                }

                WriteAtomically(targetFile, bytes);
                changed = true;
            }

            var versionText = sourceVersion ?? string.Empty;
            if (!string.Equals(installedVersion, versionText, StringComparison.Ordinal))
            {
                WriteAtomically(versionPath, Encoding.UTF8.GetBytes(versionText));
                changed = true;
            }

            if (!existed)
            {
                return InstallOutcome.Installed;
            }

            return changed ? InstallOutcome.Updated : InstallOutcome.UpToDate;
        }

        // Orders dotted versions, ignoring prerelease/build suffixes. An unparseable or
        // missing version sorts below any real one.
        internal static int CompareVersions(string left, string right)
        {
            var a = ParseVersion(left);
            var b = ParseVersion(right);
            if (a == null)
            {
                return b == null ? 0 : -1;
            }

            return b == null ? 1 : a.CompareTo(b);
        }

        private static Version ParseVersion(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var core = value.Split('-', '+')[0].Trim();
            return Version.TryParse(core, out var version) ? version : null;
        }

        // Clients may spawn the sidecar at any moment, so a half-written script must
        // never be visible: write beside the target, then swap it in.
        private static void WriteAtomically(string path, byte[] bytes)
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, bytes);
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // Leave the orphaned temp file; it is ignored by the sidecar.
                }

                throw;
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
