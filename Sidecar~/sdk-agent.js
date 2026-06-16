// Agent SDK loop — entry point for the Node sidecar.
// Launched by SidecarManager.cs with CLAUDE_CODE_OAUTH_TOKEN injected as env var.
import { query } from "@anthropic-ai/claude-agent-sdk";
import { UnityBridge } from "./unity-bridge.js";

// ── CLI args ─────────────────────────────────────────────────────────────────
const args = process.argv.slice(2);
const portIdx = args.indexOf("--port");
const port = portIdx >= 0 ? parseInt(args[portIdx + 1], 10) : 8765;

// ── Bridge ────────────────────────────────────────────────────────────────────
const bridge = new UnityBridge(port);

// Listen for chat events emitted by UnityBridge when Unity sends {type:"chat"}
bridge.on("chat", ({ id, content }) => {
  runAgent(id, content).catch((err) =>
    bridge.sendError(id, err?.message ?? String(err))
  );
});

// ── Agent loop ────────────────────────────────────────────────────────────────
async function runAgent(conversationId, userPrompt) {
  // Build dynamic tool definitions from schemas Unity registered at connect time
  const toolDefs = bridge.tools.map((t) => ({
    name: t.name,
    description: t.description,
    input_schema: t.inputSchema,
  }));

  for await (const event of query({
    prompt: userPrompt,
    options: {
      tools: toolDefs.length > 0 ? toolDefs : undefined,

      canUseTool: async (toolName, input) => {
        const schema = bridge.tools.find((t) => t.name === toolName);

        // Auto-approve read-only tools (requiresApproval === false)
        if (schema && !schema.requiresApproval)
          return { behavior: "allow" };

        // Ask Unity to show an approval dialog
        const approved = await bridge.askApproval(conversationId, toolName, input);
        return { behavior: approved ? "allow" : "deny" };
      },
    },
  })) {
    switch (event.type) {
      case "text":
        // Streaming text chunk → forward to Unity chat UI
        bridge.sendTextDelta(conversationId, event.text ?? "");
        break;

      case "tool_use": {
        const { name, input, id: toolId } = event;
        const requestId = `${conversationId}-${toolId}`;

        // Notify Unity UI that a tool is being used
        bridge.sendToolUse(conversationId, name, input);

        // Tell Unity to execute the tool; Unity will respond with tool_result
        bridge.send({
          type: "tool_execute",
          requestId,
          tool: name,
          args: JSON.stringify(input),
        });

        const result = await bridge.waitForToolResult(requestId);

        // Feed result back to Agent SDK so Claude can continue
        // (SDK contract: set event.output to continue the agentic loop)
        if (typeof event.submitOutput === "function") {
          await event.submitOutput(result);
        } else if ("output" in event) {
          event.output = result;
        }
        break;
      }

      case "error":
        bridge.sendError(conversationId, event.error?.message ?? "Unknown error");
        break;
    }
  }

  bridge.sendDone(conversationId);
}

// ── Graceful shutdown ─────────────────────────────────────────────────────────
process.on("SIGTERM", () => { bridge.close(); process.exit(0); });
process.on("SIGINT",  () => { bridge.close(); process.exit(0); });
