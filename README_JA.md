# AiGate

AiGate は、ローカル / クラウドの LLM を **統一インターフェース (`IAiClient`) で叩くための .NET ライブラリ一式** です。

- OpenAI 互換 API（/v1/chat/completions）向けの薄いクライアント
- ローカル `llamaserver`（llama.cpp server）の起動 / 終了ヘルパー
- プロファイル設定ファイル `aigate.json` による切り替え

> 実験実装です。API や構成は変更される可能性があります。

---

## プロジェクト構成

同一ソリューション内の想定:

```text
AiGate.Abstractions   // DTO / インターフェース (IAiClient など)
AiGate.Core           // OpenAI 互換クライアント & プロファイル読み込み
AiGate.LocalHost      // ローカル llamaserver の起動 / 終了
AiGate.CliTest        // 動作確認用の簡易 CLI
```

### 参照関係

- 利用側アプリ（例: Rinne CLI / GUI / テストツール）
  - `AiGate.Abstractions`
  - `AiGate.Core`
  - （ローカル LLM を自動起動するなら）`AiGate.LocalHost`

---

## 設定ファイル `aigate.json`

実行ファイルと同じディレクトリ、または任意のパスに配置します。

### 例: ローカル llama.cpp server（child プロセス起動）

```json
{
  "profiles": [
    {
      "name": "local-llama",
      "baseUrl": "http://127.0.0.1:8080/v1",
      "model": "phi-3.5-mini.gguf",
      "process": {
        "mode": "child",
        "exePath": "./ai/runtime/llama-server.exe",
        "modelPath": "./ai/models/phi-3.5-mini.gguf",
        "port": 8080,
        "extraArgs": "--ctx-size 4096"
      }
    }
  ]
}
```

- `name`  
  - プロファイル名（`AiChatRequest.Profile` で指定）。
- `baseUrl`  
  - OpenAI 互換 API の `/v1` までの URL。
- `model`  
  - サーバ側で登録されているモデル名。
- `process.mode`
  - `"child"` : AiGate 側で `llamaserver` を spawn / kill する。
  - `"external"` or 省略 : 既に起動しているサーバーに接続するだけ（プロセス管理なし）。
- `exePath`, `modelPath`  
  - AiGate 実行ファイルからの相対パスで指定。
- `extraArgs`
  - `--ctx-size` など、追加の起動引数。

### 例: クラウド (OpenAI など)

```json
{
  "profiles": [
    {
      "name": "cloud-openai",
      "baseUrl": "https://api.openai.com/v1",
      "model": "gpt-4.1-mini"
    }
  ]
}
```

API キーは JSON には書かず、環境変数などで渡す想定です（後述）。

---

## コードからの基本的な使い方

### 1. AiGateConfig の読み込み

```csharp
using System.Text.Json;
using AiGate.Core;

// configPath は "aigate.json" など
var json = await File.ReadAllTextAsync(configPath, cancellationToken);
var config = JsonSerializer.Deserialize<AiGateConfig>(
    json,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
) ?? throw new InvalidOperationException("Invalid AiGate config.");
```

### 2. IAiClient の作成

```csharp
using AiGate.Abstractions;
using AiGate.Core;

using var httpClient = new HttpClient();
var aiClient = new OpenAiCompatibleClient(httpClient, config);
```

### 3. チャットリクエストを投げる

```csharp
using AiGate.Abstractions;

var request = new AiChatRequest(
    profile: "local-llama",   // or "cloud-openai" など
    messages: new[]
    {
        AiMessage.System("You are a helpful assistant."),
        AiMessage.User("Hello, AiGate!")
    },
    MaxTokens: 128,
    Temperature: 0.7
);

var response = await aiClient.ChatAsync(request, cancellationToken);

Console.WriteLine(response.Content);
```

---

## ローカル llamaserver の起動 / 終了

`process.mode = "child"` のプロファイルについては、`LlamaServerHost` を使うことで  
**アプリ起動時に llamaserver を立ち上げ、終了時に自動で kill** できます。

### 例: プロファイル設定から LlamaServerHost を作る

```csharp
using AiGate.Core;
using AiGate.LocalHost;

// プロファイルを 1 つ選ぶ（例: "local-llama"）
var profile = config.GetProfileOrThrow("local-llama");
var baseDir = AppContext.BaseDirectory;

if (profile.Process is { } procCfg &&
    string.Equals(procCfg.Mode, "child", StringComparison.OrdinalIgnoreCase))
{
    string ResolvePath(string path)
        => Path.GetFullPath(Path.Combine(baseDir, path));

    var uri = new Uri(profile.BaseUrl);

    var options = new LlamaServerOptions
    {
        Mode = LocalProcessMode.Child,
        ExePath = ResolvePath(procCfg.ExePath!),
        ModelPath = ResolvePath(procCfg.ModelPath!),
        Port = procCfg.Port ?? uri.Port,
        Host = uri.Host,
        ExtraArgs = procCfg.ExtraArgs ?? ""
    };

    await using var llamaHost = new LlamaServerHost(options);

    // 起動 & ヘルスチェック
    await llamaHost.EnsureStartedAsync(cancellationToken);

    // ここで IAiClient 経由で好きなだけリクエストを投げる
    // ...

    // アプリ終了時 (DisposeAsync) に llamaserver を終了
}
```

- 同一プロセスの中では、`EnsureStartedAsync` を複数回呼んでも **プロセスは 1 回だけ起動**。
- アプリ終了時に `DisposeAsync` / `ShutdownAsync` を呼べば  
  `/shutdown`（あれば） → `WaitForExit` → それでも落ちない場合は `Kill(entireProcessTree: true)` という流れで終了させます。

---

## API キーの扱い（クラウド用）

`AiProfileConfig` には `ApiKey` プロパティがありますが、  
**JSON にベタ書きするのは非推奨**です。

典型的な運用:

- `aigate.json` には構造だけ書く:

  ```json
  {
    "profiles": [
      {
        "name": "cloud-openai",
        "baseUrl": "https://api.openai.com/v1",
        "model": "gpt-4.1-mini"
      }
    ]
  }
  ```

- API キーは環境変数で渡す（例）:

  - `OPENAI_API_KEY=sk-xxxx...`
  - または `AIGATE_CLOUD-OPENAI_API_KEY=sk-xxxx...`

ライブラリ側では、以下のような優先順位でキーを解決する想定です（実装例）:

1. `AiProfileConfig.ApiKey`（アプリコードから直接設定された場合）
2. 環境変数 `AIGATE_<PROFILE_NAME>_API_KEY`
3. OpenAI 用の標準環境変数 `OPENAI_API_KEY`

---

## メモ

- 現状は **非ストリーミングの `/chat/completions`** のみ対応。
- `MaxTokens` / `Temperature` は
  - `AiChatRequest` で指定すればリクエスト単位で上書き
  - `null` の場合はプロファイル側のデフォルト（将来追加予定） or ライブラリ内の簡易デフォルトが使われます。
- ctx サイズや VRAM 消費に絡む `--ctx-size` などは、  
  `process.extraArgs` で **プロファイルごとに設定**する想定です。
