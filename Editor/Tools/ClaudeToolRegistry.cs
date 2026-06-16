using System.Collections.Generic;
using System.Linq;

namespace BurgerMonster.ClaudeAgent
{
    public static class ClaudeToolRegistry
    {
        static readonly Dictionary<string, IClaudeTool> _tools = new();

        public static void Register(IClaudeTool tool) => _tools[tool.Name] = tool;
        public static void Unregister(string name) => _tools.Remove(name);

        public static IClaudeTool Get(string name) =>
            _tools.TryGetValue(name, out var t) ? t : null;

        public static IReadOnlyList<IClaudeTool> GetAll() => _tools.Values.ToList();

        // Returns JSON array of tool schemas for the sidecar to forward to the Agent SDK.
        public static string ToSchemaJson()
        {
            var entries = _tools.Values.Select(t =>
                $"{{\"name\":\"{t.Name}\",\"description\":\"{EscapeJson(t.Description)}\",\"inputSchema\":{t.InputSchemaJson},\"requiresApproval\":{(t.RequiresApproval ? "true" : "false")}}}");
            return "[" + string.Join(",", entries) + "]";
        }

        static string EscapeJson(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}
