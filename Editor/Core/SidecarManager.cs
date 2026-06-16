// Manages the Node.js sidecar process lifecycle.
// Spawns sdk-agent.js, injects OAuth token as env var, terminates on editor quit.
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BurgerMonster.ClaudeAgent
{
    [InitializeOnLoad]
    public static class SidecarManager
    {
        static Process _process;
        static string SidecarDir => Path.GetFullPath(
            Path.Combine(PackagePath, "Sidecar~"));
        static string EntryPoint => Path.Combine(SidecarDir, "sdk-agent.js");

        // Resolves the package root via its package.json
        static string PackagePath
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(SidecarManager).Assembly);
                return info?.resolvedPath ?? Path.Combine(Application.dataPath, "..", "Packages", "com.burgermonster.claude-agent");
            }
        }

        static SidecarManager()
        {
            EditorApplication.quitting += Stop;
        }

        public static bool IsRunning => _process != null && !_process.HasExited;

        public static bool Start(int port)
        {
            if (IsRunning) return true;

            var token = ClaudeAgentSettings.OAuthToken;
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning("[Claude Agent] OAuth token is not set. Go to Edit > Project Settings > Claude Agent.");
                return false;
            }

            if (!VerifyNodeInstalled())
            {
                Debug.LogError("[Claude Agent] 'node' not found in PATH. Please install Node.js (https://nodejs.org).");
                return false;
            }

            EnsureNpmInstalled();

            var psi = new ProcessStartInfo
            {
                FileName               = "node",
                Arguments              = $"\"{EntryPoint}\" --port {port}",
                WorkingDirectory       = SidecarDir,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            psi.EnvironmentVariables["CLAUDE_CODE_OAUTH_TOKEN"] = token;
            // Never set ANTHROPIC_API_KEY — subscription credits take priority.

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log($"[Sidecar] {e.Data}"); };
            _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) Debug.LogWarning($"[Sidecar] {e.Data}"); };
            _process.Exited             += (_, _) => MainThreadDispatcher.Enqueue(() =>
                Debug.Log("[Claude Agent] Sidecar exited."));

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Debug.Log($"[Claude Agent] Sidecar started (PID {_process.Id}) on port {port}.");
            return true;
        }

        public static void Stop()
        {
            if (!IsRunning) return;
            try { _process.Kill(); }
            catch { /* already exited */ }
            _process = null;
            Debug.Log("[Claude Agent] Sidecar stopped.");
        }

        static bool VerifyNodeInstalled()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("node", "--version")
                    { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        static void EnsureNpmInstalled()
        {
            var modules = Path.Combine(SidecarDir, "node_modules");
            if (Directory.Exists(modules)) return;

            Debug.Log("[Claude Agent] Running npm install for sidecar...");
            var p = Process.Start(new ProcessStartInfo("npm", "install")
            {
                WorkingDirectory       = SidecarDir,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true,
            });
            p.WaitForExit(60000);
        }
    }
}
