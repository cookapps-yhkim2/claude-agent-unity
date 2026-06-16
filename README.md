# Claude Agent — Unity Package

Unity Editor 안에서 Claude와 자연어로 대화하고 씬을 조작하는 AI 호스트 패키지입니다.

## 구조

```
Unity Editor (C#) ←──WebSocket──→ Node.js Sidecar ←──HTTPS──→ Claude API
     호스트 (UI + 실행 + 승인)        Agent SDK 루프
```

## 빠른 시작

### 1. Node.js 설치 확인 (≥ 18)
```bash
node --version
```

### 2. OAuth 토큰 발급 (팀원 1인당 1회)
```bash
npm install -g @anthropic-ai/claude-code
claude setup-token
```

### 3. 토큰 등록
`Edit > Project Settings > Claude Agent` 에서 토큰을 입력하고 **Save Token** 클릭.

> ⚠️ 토큰은 EditorPrefs에 머신별로 저장됩니다. Git에 커밋하지 마세요.

### 4. 창 열기
`Window > Claude Agent` → **연결** 버튼 클릭.

첫 실행 시 `Sidecar~/node_modules` 설치가 자동으로 진행됩니다 (약 10–30초).

---

## 커스텀 툴 추가

`IClaudeTool`을 구현하고 `ClaudeToolRegistry.Register()`로 등록합니다.

```csharp
using BurgerMonster.ClaudeAgent;
using System.Threading.Tasks;

public class MyTool : IClaudeTool
{
    public string Name           => "my_tool";
    public string Description    => "내 커스텀 툴";
    public bool   RequiresApproval => true;
    public string InputSchemaJson  => @"{
        ""type"": ""object"",
        ""properties"": { ""param"": { ""type"": ""string"" } },
        ""required"": [""param""]
    }";

    public Task<string> ExecuteAsync(string inputJson)
    {
        // inputJson: Claude가 생성한 JSON 파라미터
        return Task.FromResult("툴 실행 결과");
    }
}

// EditorWindow.OnEnable 또는 InitializeOnLoad 에서 등록
ClaudeToolRegistry.Register(new MyTool());
```

---

## 플랜별 월 크레딧

| 플랜 | 월 크레딧 |
|------|-----------|
| Pro  | $20       |
| Max 5x | $100    |
| Max 20x | $200   |

---

## 아키텍처 참고

- [Agent SDK Overview](https://code.claude.com/docs/en/agent-sdk/overview)
- [CoderGamester/mcp-unity](https://github.com/CoderGamester/mcp-unity) — 통신 구조 참고
