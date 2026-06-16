using System;
using UnityEditor;

namespace BurgerMonster.ClaudeAgent
{
    public static class ApprovalDialog
    {
        /// <summary>
        /// Shows a blocking approval dialog on the main thread.
        /// Returns true if the user approved.
        /// </summary>
        public static bool Ask(string toolName, string argsJson)
        {
            return EditorUtility.DisplayDialog(
                "Claude Agent — 권한 요청",
                $"Claude가 다음 도구를 실행하려고 합니다:\n\n" +
                $"도구: {toolName}\n" +
                $"입력:\n{PrettyArgs(argsJson)}",
                "승인",
                "거부"
            );
        }

        static string PrettyArgs(string json)
        {
            // Minimal pretty-print: insert newline after each comma at top level
            if (string.IsNullOrEmpty(json)) return "(없음)";
            try
            {
                return json
                    .Replace("{", "")
                    .Replace("}", "")
                    .Replace(",\"", "\n")
                    .Replace("\":", ": ")
                    .Replace("\"", "")
                    .Trim();
            }
            catch { return json; }
        }
    }
}
