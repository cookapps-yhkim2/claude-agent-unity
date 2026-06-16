// Wire protocol between Unity (C#) and Node sidecar (unity-bridge.js).
// CANONICAL source — mirror field names verbatim in Sidecar~/unity-bridge.js.
using System;

namespace BurgerMonster.ClaudeAgent
{
    // ── Unity → Node ────────────────────────────────────────────────────────

    [Serializable]
    public class ChatRequest
    {
        public string type = "chat";
        public string id;
        public string content;
    }

    [Serializable]
    public class ApprovalResponse
    {
        public string type = "approval_response";
        public string requestId;
        public bool approved;
    }

    [Serializable]
    public class RegisterToolsMessage
    {
        public string type  = "register_tools";
        public string tools;  // JSON array string from ClaudeToolRegistry.ToSchemaJson()
    }

    [Serializable]
    public class ToolResultMessage
    {
        public string type      = "tool_result";
        public string requestId;
        public string result;
    }

    // ── Node → Unity ────────────────────────────────────────────────────────

    [Serializable]
    public class IncomingMessage
    {
        // Discriminator field — matches on "type"
        public string type;    // text_delta | tool_use | approval_request | done | error
        public string id;

        // text_delta
        public string text;

        // tool_use
        public string tool;
        public string args;    // raw JSON string

        // approval_request / tool_execute
        public string requestId;

        // tool_execute (sent after approval — tells Unity to actually run the tool)
        // tool + args fields are reused from tool_use above

        // error
        public string message;
    }
}
