// WebSocket server (Node side). Mirrors MessageProtocol.cs wire contract.
// CANONICAL field names are defined in Editor/Bridge/MessageProtocol.cs — keep in sync.
import { WebSocketServer, WebSocket } from "ws";
import { EventEmitter } from "events";

export class UnityBridge extends EventEmitter {
  /** @type {WebSocket|null} */
  #client = null;
  /** @type {WebSocketServer} */
  #wss;
  /** @type {Map<string, {resolve:(v:boolean)=>void, timer:ReturnType<typeof setTimeout>}>} */
  #pendingApprovals = new Map();
  /** @type {Map<string, {resolve:(v:string)=>void, timer:ReturnType<typeof setTimeout>}>} */
  #pendingToolResults = new Map();
  /** @type {Array<{name:string,description:string,inputSchema:object,requiresApproval:boolean}>} */
  #tools = [];

  /** @param {number} port */
  constructor(port) {
    super();
    this.#wss = new WebSocketServer({ port });
    this.#wss.on("connection", (ws) => {
      this.#client = ws;
      console.log("[Bridge] Unity connected.");
      ws.on("message", (raw) => this.#handleIncoming(JSON.parse(raw.toString())));
      ws.on("close", () => { this.#client = null; console.log("[Bridge] Unity disconnected."); });
    });
    console.log(`[Bridge] WS server listening on ws://localhost:${port}`);
  }

  get tools() { return this.#tools; }

  // ── Inbound from Unity ───────────────────────────────────────────────────

  #handleIncoming(msg) {
    switch (msg.type) {
      case "chat":
        // Emits: bridge.on("chat", ({id, content}) => ...)
        this.emit("chat", { id: msg.id, content: msg.content });
        break;

      case "register_tools":
        try { this.#tools = JSON.parse(msg.tools); }
        catch { console.warn("[Bridge] Failed to parse tool schemas"); }
        break;

      case "approval_response": {
        const pending = this.#pendingApprovals.get(msg.requestId);
        if (pending) {
          clearTimeout(pending.timer);
          this.#pendingApprovals.delete(msg.requestId);
          pending.resolve(msg.approved === true);
        }
        break;
      }

      case "tool_result": {
        const pending = this.#pendingToolResults.get(msg.requestId);
        if (pending) {
          clearTimeout(pending.timer);
          this.#pendingToolResults.delete(msg.requestId);
          pending.resolve(msg.result ?? "");
        }
        break;
      }
    }
  }

  // ── Outbound to Unity ────────────────────────────────────────────────────

  send(payload) {
    if (this.#client?.readyState === WebSocket.OPEN)
      this.#client.send(JSON.stringify(payload));
  }

  sendTextDelta(id, text)         { this.send({ type: "text_delta", id, text }); }
  sendToolUse(id, tool, args)     { this.send({ type: "tool_use", id, tool, args: JSON.stringify(args) }); }
  sendDone(id)                    { this.send({ type: "done", id }); }
  sendError(id, message)          { this.send({ type: "error", id, message }); }

  /**
   * Ask Unity for approval; auto-denies after timeoutMs if no response.
   * @returns {Promise<boolean>}
   */
  askApproval(id, tool, args, timeoutMs = 30_000) {
    const requestId = `${id}-${tool}-${Date.now()}`;
    this.send({ type: "approval_request", id, requestId, tool, args: JSON.stringify(args) });

    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        this.#pendingApprovals.delete(requestId);
        console.warn(`[Bridge] Approval timeout for ${tool} — auto-denying.`);
        resolve(false);
      }, timeoutMs);
      this.#pendingApprovals.set(requestId, { resolve, timer });
    });
  }

  /**
   * Tell Unity to execute a tool and wait for the result string.
   * requestId must match what Unity's tool_result response will carry.
   * @returns {Promise<string>}
   */
  waitForToolResult(requestId, timeoutMs = 30_000) {
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        this.#pendingToolResults.delete(requestId);
        resolve("오류: 툴 실행 시간 초과");
      }, timeoutMs);
      this.#pendingToolResults.set(requestId, {
        resolve: (v) => { clearTimeout(timer); resolve(v); },
      });
    });
  }

  close() { this.#wss.close(); }
}
