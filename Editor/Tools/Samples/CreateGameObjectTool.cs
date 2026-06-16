using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace BurgerMonster.ClaudeAgent.Samples
{
    public class CreateGameObjectTool : IClaudeTool
    {
        public string Name        => "create_game_object";
        public string Description => "Unity 씬에 기본 도형 GameObject를 생성합니다.";
        public bool RequiresApproval => true;
        public string InputSchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""name"":   { ""type"": ""string"", ""description"": ""GameObject 이름"" },
    ""prefab"": { ""type"": ""string"", ""enum"": [""cube"", ""sphere"", ""capsule"", ""cylinder"", ""plane"", ""camera""] }
  },
  ""required"": [""name"", ""prefab""]
}";

        public Task<string> ExecuteAsync(string inputJson)
        {
            var tcs = new TaskCompletionSource<string>();
            MainThreadDispatcher.Enqueue(() =>
            {
                try
                {
                    var args = JsonUtility.FromJson<Args>(inputJson);
                    var go   = CreatePrimitive(args.prefab);
                    if (go == null)
                    {
                        tcs.SetResult($"알 수 없는 prefab 타입: {args.prefab}");
                        return;
                    }
                    go.name = args.name;
                    Undo.RegisterCreatedObjectUndo(go, $"Claude: Create {args.name}");
                    tcs.SetResult($"GameObject '{go.name}' (id={go.GetInstanceID()}) 를 씬에 생성했습니다.");
                }
                catch (Exception ex)
                {
                    tcs.SetResult($"오류: {ex.Message}");
                }
            });
            return tcs.Task;
        }

        static GameObject CreatePrimitive(string prefab) => prefab switch
        {
            "cube"     => GameObject.CreatePrimitive(PrimitiveType.Cube),
            "sphere"   => GameObject.CreatePrimitive(PrimitiveType.Sphere),
            "capsule"  => GameObject.CreatePrimitive(PrimitiveType.Capsule),
            "cylinder" => GameObject.CreatePrimitive(PrimitiveType.Cylinder),
            "plane"    => GameObject.CreatePrimitive(PrimitiveType.Plane),
            "camera"   => new GameObject("Camera", typeof(Camera)),
            _          => null,
        };

        [Serializable]
        class Args { public string name; public string prefab; }
    }
}
