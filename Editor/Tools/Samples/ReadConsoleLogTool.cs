using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace BurgerMonster.ClaudeAgent.Samples
{
    public class ReadConsoleLogTool : IClaudeTool
    {
        public string Name           => "read_console_log";
        public string Description    => "Unity 콘솔의 최근 로그 메시지를 반환합니다. 읽기 전용 — 자동 승인됩니다.";
        public bool   RequiresApproval => false;
        public string InputSchemaJson  => @"{
  ""type"": ""object"",
  ""properties"": {
    ""lines"": { ""type"": ""integer"", ""description"": ""반환할 최대 로그 수 (기본 20)"", ""default"": 20 }
  }
}";

        static readonly List<string> _log = new();
        static bool _hooked;

        public ReadConsoleLogTool()
        {
            if (_hooked) return;
            _hooked = true;
            Application.logMessageReceived += (condition, stackTrace, type) =>
            {
                var prefix = type switch
                {
                    LogType.Error     => "[Error]",
                    LogType.Warning   => "[Warning]",
                    LogType.Exception => "[Exception]",
                    _                 => "[Info]",
                };
                _log.Add($"{prefix} {condition}");
                if (_log.Count > 200) _log.RemoveAt(0);  // rolling buffer
            };
        }

        public Task<string> ExecuteAsync(string inputJson)
        {
            var tcs = new TaskCompletionSource<string>();
            MainThreadDispatcher.Enqueue(() =>
            {
                try
                {
                    var args  = JsonUtility.FromJson<Args>(inputJson ?? "{}");
                    int lines = args.lines > 0 ? args.lines : 20;
                    int start = Math.Max(0, _log.Count - lines);
                    tcs.SetResult(string.Join("\n", _log.GetRange(start, _log.Count - start)));
                }
                catch (Exception ex)
                {
                    tcs.SetResult($"오류: {ex.Message}");
                }
            });
            return tcs.Task;
        }

        [Serializable]
        class Args { public int lines; }
    }
}
