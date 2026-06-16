// Example tool — mirrors CreateGameObjectTool.cs on the C# side.
// The actual execution happens in Unity; this file provides the schema
// so the sidecar can forward it to the Agent SDK before Unity connects.
export default {
  name: "create_game_object",
  description: "Unity 씬에 기본 도형 GameObject를 생성합니다.",
  requiresApproval: true,
  inputSchema: {
    type: "object",
    properties: {
      name:   { type: "string", description: "GameObject 이름" },
      prefab: { type: "string", enum: ["cube", "sphere", "capsule", "cylinder", "plane", "camera"] },
    },
    required: ["name", "prefab"],
  },
  // handler is called only if Unity isn't connected (fallback / testing)
  handler: async (input) => ({
    content: [{ type: "text", text: `[mock] ${input.name}(${input.prefab}) 생성 요청 수신` }],
  }),
};
