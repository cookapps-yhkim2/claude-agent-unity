using System.Threading.Tasks;

namespace BurgerMonster.ClaudeAgent
{
    /// <summary>
    /// Implement this interface and register via ClaudeToolRegistry to expose
    /// a custom tool to the sidecar agent loop.
    /// </summary>
    public interface IClaudeTool
    {
        /// Tool name sent to Claude (snake_case, e.g. "create_game_object").
        string Name { get; }

        /// Human-readable description shown to Claude in the tool schema.
        string Description { get; }

        /// JSON Schema string for the tool's input parameters.
        string InputSchemaJson { get; }

        /// When true, a user-approval dialog appears before execution.
        bool RequiresApproval { get; }

        /// Execute the tool. inputJson is the raw JSON from Claude's tool_use block.
        /// Return a plain-text result string that gets sent back to Claude.
        Task<string> ExecuteAsync(string inputJson);
    }
}
