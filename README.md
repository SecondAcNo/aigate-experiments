# AiGate

AiGate is a small .NET library set that lets you call **local and cloud LLMs through a single interface (`IAiClient`)**.

- Thin client for OpenAI-compatible APIs (`/v1/chat/completions`)
- Helper to start/stop a local `llamaserver` (llama.cpp server)
- Profile-based configuration via `aigate.json`

> This is an experimental implementation. APIs and internal structure may change.

---

## Project Layout

Typical solution layout:

```text
AiGate.Abstractions   // DTOs / interfaces (IAiClient, AiChatRequest, etc.)
AiGate.Core           // OpenAI-compatible client & profile loading
AiGate.LocalHost      // Local llamaserver process host (start / stop)
AiGate.CliTest        // Simple CLI for manual testing
```

### References

Your app (e.g. Rinne CLI / GUI / test tool) normally references:

- `AiGate.Abstractions`
- `AiGate.Core`
- `AiGate.LocalHost` (only if you want AiGate to manage local LLM processes)

---

## Config File: `aigate.json`

Place `aigate.json` next to your executable (or specify the path yourself).

### Example: local llama.cpp server (child process)

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
  Profile name (used by `AiChatRequest.Profile`).
- `baseUrl`  
  Base URL of the OpenAI-compatible API up to `/v1`.
- `model`  
  Model ID known by the server.
- `process.mode`
  - `"child"` : AiGate spawns and kills `llamaserver` as a child process.
  - `"external"` or omitted : AiGate only connects to an already running server (no process management).
- `exePath`, `modelPath`  
  Paths are resolved relative to the AiGate executable.
- `extraArgs`  
  Additional start arguments such as `--ctx-size`.

### Example: cloud (OpenAI, etc.)

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

API keys are **not** stored in JSON. Use environment variables or another secret source (see below).

---

## Basic Usage from Code

### 1. Load `AiGateConfig`

```csharp
using System.Text.Json;
using AiGate.Core;

// configPath is something like "aigate.json"
var json = await File.ReadAllTextAsync(configPath, cancellationToken);
var config = JsonSerializer.Deserialize<AiGateConfig>(
    json,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
) ?? throw new InvalidOperationException("Invalid AiGate config.");
```

### 2. Create an `IAiClient`

```csharp
using AiGate.Abstractions;
using AiGate.Core;

using var httpClient = new HttpClient();
var aiClient = new OpenAiCompatibleClient(httpClient, config);
```

### 3. Send a chat request

```csharp
using AiGate.Abstractions;

var request = new AiChatRequest(
    profile: "local-llama",   // or "cloud-openai", etc.
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

## Managing a Local `llamaserver` Process

For profiles with `process.mode = "child"`, you can use `LlamaServerHost` to:

- start `llamaserver` at app startup,
- wait until it becomes healthy,
- shut it down automatically when the app exits.

### Example: build a `LlamaServerHost` from profile config

```csharp
using AiGate.Core;
using AiGate.LocalHost;

// pick a profile (e.g. "local-llama")
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

    // start & wait until healthy
    await llamaHost.EnsureStartedAsync(cancellationToken);

    // now you can send as many requests as you like via IAiClient
    // ...

    // when the app exits (DisposeAsync), llamaserver will be stopped:
    //   /shutdown (if available) -> WaitForExit -> Kill(entireProcessTree: true) as a last resort
}
```

- Within a single process, multiple calls to `EnsureStartedAsync` will reuse the same child process.
- On app shutdown, `DisposeAsync` / `ShutdownAsync` cleans up the server.

---

## API Keys (cloud profiles)

`AiProfileConfig` still has an `ApiKey` property, but **storing keys inside JSON is not recommended**.

Typical pattern:

- `aigate.json` only holds structure:

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

- API keys go into environment variables, e.g.:

  - `OPENAI_API_KEY=sk-xxxx...`
  - or `AIGATE_CLOUD-OPENAI_API_KEY=sk-xxxx...`

In code, AiGate can resolve keys in this order (example implementation):

1. `AiProfileConfig.ApiKey` (set directly from your app)
2. Environment variable `AIGATE_<PROFILE_NAME>_API_KEY`
3. Common provider-specific variable like `OPENAI_API_KEY`

This keeps `aigate.json` safe to commit while still allowing per-profile secrets.

---

## Notes

- Currently only **non-streaming** `/chat/completions` is supported.
- `MaxTokens` / `Temperature`:
  - Set them on `AiChatRequest` to override per-request.
  - Leave them `null` to use profile defaults (planned) or simple built-in defaults.
- Context length / VRAM-heavy options (like `--ctx-size`) are **process-level settings** and should be configured via `process.extraArgs` on each profile.
