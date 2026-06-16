using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using BurgerMonster.ClaudeAgent.Samples;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BurgerMonster.ClaudeAgent
{
    public class ClaudeAgentWindow : EditorWindow
    {
        [MenuItem("Window/Claude Agent")]
        static void Open() => GetWindow<ClaudeAgentWindow>("Claude Agent");

        // ── State ────────────────────────────────────────────────────────────
        readonly List<ChatMessage> _messages = new();
        WebSocketBridge _bridge;
        string _input = "";
        Vector2 _scroll;
        bool _connecting;
        string _partialAssistant; // accumulated text_delta for current response
        string _pendingApprovalRequestId;

        // Auth flow
        bool _authRunning;
        string _tokenPaste = "";
        Process _authProcess;

        // ── Styles (lazy) ────────────────────────────────────────────────────
        GUIStyle _userStyle, _assistStyle, _sysStyle;

        // ── Lifecycle ────────────────────────────────────────────────────────
        void OnEnable()
        {
            // Register default sample tools
            ClaudeToolRegistry.Register(new CreateGameObjectTool());
            ClaudeToolRegistry.Register(new ReadConsoleLogTool());
        }

        void OnDisable()
        {
            _bridge?.Dispose();
            _bridge = null;
            SidecarManager.Stop();
        }

        // ── GUI ──────────────────────────────────────────────────────────────
        void OnGUI()
        {
            EnsureStyles();

            // Header bar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Claude Agent", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            var status = _bridge?.IsConnected == true ? "● 연결됨" : "○ 끊김";
            var statusColor = _bridge?.IsConnected == true ? Color.green : Color.gray;
            var prev = GUI.contentColor;
            GUI.contentColor = statusColor;
            GUILayout.Label(status, EditorStyles.miniLabel);
            GUI.contentColor = prev;

            if (GUILayout.Button("설정", EditorStyles.toolbarButton, GUILayout.Width(40)))
                SettingsService.OpenProjectSettings("Project/Claude Agent");
            EditorGUILayout.EndHorizontal();

            // Connect prompt if not connected
            if (_bridge?.IsConnected != true)
                DrawConnectPrompt();

            // Chat log
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (var msg in _messages)
            {
                var style = msg.Role switch
                {
                    ChatRole.User      => _userStyle,
                    ChatRole.Assistant => _assistStyle,
                    _                  => _sysStyle,
                };
                EditorGUILayout.LabelField(
                    msg.Role == ChatRole.User ? $"👤  {msg.Text}"
                    : msg.Role == ChatRole.Assistant ? $"🤖  {msg.Text}"
                    : $"ℹ  {msg.Text}",
                    style);
            }

            // Show partial streaming text
            if (!string.IsNullOrEmpty(_partialAssistant))
                EditorGUILayout.LabelField($"🤖  {_partialAssistant}…", _assistStyle);

            EditorGUILayout.EndScrollView();

            // Input row
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _bridge?.IsConnected == true && _pendingApprovalRequestId == null;
            _input = EditorGUILayout.TextField(_input);
            if ((GUILayout.Button("전송", GUILayout.Width(52)) ||
                 (Event.current.type == EventType.KeyDown &&
                  Event.current.keyCode == KeyCode.Return &&
                  GUI.GetNameOfFocusedControl() == "chat-input")) &&
                !string.IsNullOrWhiteSpace(_input))
            {
                _ = SendMessageAsync(_input);
                _input = "";
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        // ── Connect prompt ───────────────────────────────────────────────────
        void DrawConnectPrompt()
        {
            bool hasToken = !string.IsNullOrEmpty(ClaudeAgentSettings.OAuthToken);

            if (!hasToken)
            {
                DrawAuthSection();
                return;
            }

            EditorGUILayout.HelpBox("사이드카에 연결되지 않았습니다.", MessageType.Info);
            GUI.enabled = !_connecting;
            if (GUILayout.Button(_connecting ? "연결 중…" : "연결"))
                _ = ConnectAsync();
            GUI.enabled = true;
        }

        void DrawAuthSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Claude 계정 연동", EditorStyles.boldLabel);

            if (_authRunning)
            {
                EditorGUILayout.HelpBox(
                    "브라우저에서 로그인을 완료해 주세요.\n" +
                    "완료 후 토큰이 자동으로 감지됩니다. 감지되지 않으면 아래에 붙여넣기 하세요.",
                    MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                _tokenPaste = EditorGUILayout.PasswordField("토큰 붙여넣기", _tokenPaste);
                if (GUILayout.Button("저장", GUILayout.Width(48)) &&
                    !string.IsNullOrWhiteSpace(_tokenPaste))
                    ApplyToken(_tokenPaste.Trim());
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Claude Pro / Max 구독 계정으로 로그인하면 Unity Editor에서 Claude를 사용할 수 있습니다.\n\n" +
                    "필요 사항: claude CLI 설치\n" +
                    "npm install -g @anthropic-ai/claude-code",
                    MessageType.Info);

                if (GUILayout.Button("Claude 계정 연동", GUILayout.Height(28)))
                    StartAuthFlow();

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("이미 토큰이 있으신가요?", EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                _tokenPaste = EditorGUILayout.PasswordField(_tokenPaste);
                if (GUILayout.Button("저장", GUILayout.Width(48)) &&
                    !string.IsNullOrWhiteSpace(_tokenPaste))
                    ApplyToken(_tokenPaste.Trim());
                EditorGUILayout.EndHorizontal();
            }
        }

        void StartAuthFlow()
        {
            _authRunning = true;
            _tokenPaste  = "";
            Repaint();

            try
            {
                var psi = new ProcessStartInfo("claude", "setup-token")
                {
                    UseShellExecute = true,  // interactive; opens in its own terminal/browser
                };
                _authProcess = Process.Start(psi);
                if (_authProcess != null)
                {
                    _authProcess.EnableRaisingEvents = true;
                    _authProcess.Exited += (_, _) =>
                        MainThreadDispatcher.Enqueue(OnAuthProcessExited);
                }
            }
            catch (Exception ex)
            {
                _authRunning = false;
                AddSystem($"오류: claude CLI를 실행할 수 없습니다 — {ex.Message}");
                AddSystem("npm install -g @anthropic-ai/claude-code 를 먼저 실행해 주세요.");
                Repaint();
            }
        }

        void OnAuthProcessExited()
        {
            var token = ClaudeAgentSettings.TryReadClaudeConfigToken();
            if (!string.IsNullOrEmpty(token))
            {
                ApplyToken(token);
            }
            else
            {
                // Keep _authRunning=true so the paste field stays visible
                AddSystem("토큰을 자동으로 읽지 못했습니다. 아래 필드에 직접 붙여넣기 해주세요.");
                Repaint();
            }
        }

        void ApplyToken(string token)
        {
            ClaudeAgentSettings.OAuthToken = token;
            _tokenPaste  = "";
            _authRunning = false;
            AddSystem("✅ 토큰이 저장되었습니다. 연결 버튼을 눌러 시작하세요.");
            Repaint();
        }

        // ── Connect ──────────────────────────────────────────────────────────
        async Task ConnectAsync()
        {
            _connecting = true;
            var port = ClaudeAgentSettings.SidecarPort;

            if (!SidecarManager.IsRunning)
                SidecarManager.Start(port);

            // Give the sidecar a moment to start its WS server
            await Task.Delay(1500);

            _bridge = new WebSocketBridge(port);
            _bridge.OnMessage     += HandleMessage;
            _bridge.OnError       += e => AddSystem($"오류: {e}");
            _bridge.OnDisconnected += () => { AddSystem("사이드카 연결 끊김"); Repaint(); };

            try
            {
                await _bridge.ConnectAsync();
                // Send tool schema so sidecar can forward it to the Agent SDK
                await _bridge.SendAsync(new RegisterToolsMessage { tools = ClaudeToolRegistry.ToSchemaJson() });
                AddSystem("Claude Agent에 연결되었습니다.");
            }
            catch (Exception ex)
            {
                AddSystem($"연결 실패: {ex.Message}");
                _bridge.Dispose();
                _bridge = null;
            }
            finally
            {
                _connecting = false;
                Repaint();
            }
        }

        // ── Send ─────────────────────────────────────────────────────────────
        async Task SendMessageAsync(string text)
        {
            _messages.Add(new ChatMessage(ChatRole.User, text));
            _partialAssistant = "";
            Repaint();

            await _bridge.SendAsync(new ChatRequest
            {
                id      = Guid.NewGuid().ToString(),
                content = text,
            });
        }

        // ── Receive ──────────────────────────────────────────────────────────
        void HandleMessage(IncomingMessage msg)
        {
            switch (msg.type)
            {
                case "text_delta":
                    _partialAssistant += msg.text;
                    break;

                case "done":
                    if (!string.IsNullOrEmpty(_partialAssistant))
                        _messages.Add(new ChatMessage(ChatRole.Assistant, _partialAssistant));
                    _partialAssistant = "";
                    break;

                case "tool_use":
                    AddSystem($"🔧 tool_use → {msg.tool}({msg.args})");
                    break;

                case "approval_request":
                    HandleApprovalRequest(msg);
                    break;

                case "tool_execute":
                    _ = HandleToolExecuteAsync(msg);
                    break;

                case "error":
                    AddSystem($"❌ {msg.message}");
                    _partialAssistant = "";
                    break;
            }
            Repaint();
        }

        // Step 1 — approval gate (canUseTool in sidecar)
        void HandleApprovalRequest(IncomingMessage msg)
        {
            _pendingApprovalRequestId = msg.requestId;
            Repaint();

            var tool    = ClaudeToolRegistry.Get(msg.tool);
            bool approved = tool?.RequiresApproval == false
                || ApprovalDialog.Ask(msg.tool, msg.args);

            _ = _bridge.SendAsync(new ApprovalResponse
            {
                requestId = msg.requestId,
                approved  = approved,
            });

            if (!approved)
            {
                AddSystem("⛔ 작업이 거부되었습니다.");
                _pendingApprovalRequestId = null;
                Repaint();
            }
        }

        // Step 2 — actual execution (after sidecar's tool handler fires)
        async Task HandleToolExecuteAsync(IncomingMessage msg)
        {
            var tool = ClaudeToolRegistry.Get(msg.tool);
            string result;

            if (tool == null)
                result = $"오류: 등록되지 않은 툴 '{msg.tool}'";
            else
                result = await tool.ExecuteAsync(msg.args);

            await _bridge.SendAsync(new ToolResultMessage { requestId = msg.requestId, result = result });

            _pendingApprovalRequestId = null;
            Repaint();
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        void AddSystem(string text) => _messages.Add(new ChatMessage(ChatRole.System, text));

        void EnsureStyles()
        {
            if (_userStyle != null) return;

            _userStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                normal   = { textColor = new Color(0.3f, 0.6f, 0.9f) },
                padding  = new RectOffset(4, 4, 2, 2),
            };
            _assistStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                padding = new RectOffset(4, 4, 2, 2),
            };
            _sysStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                normal  = { textColor = Color.gray },
                fontSize = 11,
                padding  = new RectOffset(4, 4, 1, 1),
            };
        }
    }
}
