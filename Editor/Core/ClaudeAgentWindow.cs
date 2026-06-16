using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BurgerMonster.ClaudeAgent.Samples;
using UnityEditor;
using UnityEngine;

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
            {
                EditorGUILayout.HelpBox(
                    "사이드카에 연결되지 않았습니다.\n" +
                    "Edit > Project Settings > Claude Agent 에서 OAuth 토큰을 설정하고 연결하세요.",
                    MessageType.Info);

                GUI.enabled = !_connecting;
                if (GUILayout.Button(_connecting ? "연결 중…" : "연결"))
                    _ = ConnectAsync();
                GUI.enabled = true;
            }

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
