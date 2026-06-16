// Example tool — mirrors ReadConsoleLogTool.cs on the C# side.
export default {
  name: "read_console_log",
  description: "Unity 콘솔의 최근 로그 메시지를 반환합니다. 읽기 전용 — 자동 승인됩니다.",
  requiresApproval: false,
  inputSchema: {
    type: "object",
    properties: {
      lines: { type: "integer", description: "반환할 최대 로그 수 (기본 20)", default: 20 },
    },
  },
  handler: async () => ({
    content: [{ type: "text", text: "[mock] Unity 콘솔에 연결되지 않은 상태입니다." }],
  }),
};
