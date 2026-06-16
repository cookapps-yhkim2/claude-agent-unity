using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace BurgerMonster.ClaudeAgent
{
    public static class ClaudeAgentSettings
    {
        const string TokenKey = "ClaudeAgent.OAuthToken";
        const int DefaultPort = 8765;
        const string PortKey  = "ClaudeAgent.SidecarPort";

        public static string OAuthToken
        {
            get => EditorPrefs.GetString(TokenKey, "");
            set => EditorPrefs.SetString(TokenKey, value);
        }

        public static int SidecarPort
        {
            get => EditorPrefs.GetInt(PortKey, DefaultPort);
            set => EditorPrefs.SetInt(PortKey, value);
        }

        // Tries to read the OAuth token Claude CLI stored after `claude setup-token`.
        // Returns null if not found.
        public static string TryReadClaudeConfigToken()
        {
            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            var candidates = new[]
            {
                Path.Combine(home, ".claude", ".credentials.json"),
                Path.Combine(home, ".claude", "auth.json"),
                Path.Combine(home, ".claude", "settings.json"),
            };
            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var text = File.ReadAllText(path);
                    var m = Regex.Match(text,
                        @"""(?:oauthToken|claudeAiOauthToken|accessToken|token)""\s*:\s*""([^""]+)""");
                    if (m.Success) return m.Groups[1].Value;
                }
                catch { /* ignore */ }
            }
            return null;
        }
    }

    // Project Settings → Claude Agent
    static class ClaudeAgentSettingsProvider
    {
        [SettingsProvider]
        static SettingsProvider Create() =>
            new SettingsProvider("Project/Claude Agent", SettingsScope.User)
            {
                label = "Claude Agent",
                guiHandler = _ => DrawGUI(),
            };

        static string _tokenInput;
        static bool _showToken;

        static void DrawGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Authentication", EditorStyles.boldLabel);

            if (_tokenInput == null)
                _tokenInput = ClaudeAgentSettings.OAuthToken;

            EditorGUILayout.HelpBox(
                "Obtain your token with: claude setup-token\n" +
                "Stored in EditorPrefs (machine-local). Never commit to version control.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            _tokenInput = _showToken
                ? EditorGUILayout.TextField("OAuth Token", _tokenInput)
                : EditorGUILayout.PasswordField("OAuth Token", _tokenInput);
            if (GUILayout.Button(_showToken ? "Hide" : "Show", GUILayout.Width(48)))
                _showToken = !_showToken;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Token"))
            {
                ClaudeAgentSettings.OAuthToken = _tokenInput;
                EditorUtility.DisplayDialog("Claude Agent", "Token saved.", "OK");
            }
            if (GUILayout.Button("Clear Token"))
            {
                _tokenInput = "";
                ClaudeAgentSettings.OAuthToken = "";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Sidecar", EditorStyles.boldLabel);
            int port = EditorGUILayout.IntField("WebSocket Port", ClaudeAgentSettings.SidecarPort);
            if (port != ClaudeAgentSettings.SidecarPort)
                ClaudeAgentSettings.SidecarPort = port;
        }
    }
}
